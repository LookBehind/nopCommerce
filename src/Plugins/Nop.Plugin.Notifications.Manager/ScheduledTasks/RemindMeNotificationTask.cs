using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Catalog;
using Nop.Services.Companies;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Tasks;
using Nop.Web.Areas.Admin.Models.Orders;
using TimeZoneConverter;
using ILogger = Nop.Services.Logging.ILogger;
using Message = FirebaseAdmin.Messaging.Message;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks
{
    /// <summary>
    /// Represents a task for sending reminding notification to customer
    /// </summary>
    public class RemindMeNotificationTask : IScheduleTask
    {
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly ICompanyService _companyService;
        private readonly KubeAiChatClient _kubeAiChatClient;
        private readonly IRepository<ProductReview> _productReviewRepository;
        private readonly ILogger _logger;
        private readonly PushNotificationService _pushNotificationService;
        private readonly CatalogSettings _catalogSettings;

        /// <summary>
        /// Reminder-time slot granularity in minutes. The mobile picker offers 15-minute slots and this task
        /// is registered as a Hangfire recurring job on the matching "*/15 * * * *" CRON.
        /// </summary>
        private const int SLOT_MINUTES = 15;

        private const string LLM_MODEL = "gemma-4-31b-it-awq";

        /// <summary>
        /// Hard ceiling on the whole run, measured from ExecuteAsync entry. Leaves a 5-minute
        /// buffer under the 20-minute "don't delay the reminder" SLA for push delivery and
        /// per-customer DB/notification overhead. Nearly all eligible customers (135/136 as of
        /// 2026-07-29) share the same default reminder slot, so this run's customer count can be
        /// large - this deadline is what actually protects the SLA, not per-call timeouts alone.
        /// </summary>
        private static readonly TimeSpan GLOBAL_DEADLINE = TimeSpan.FromMinutes(15);

        /// <summary>
        /// How long we're willing to wait, once, for the model to come out of a cold start before
        /// giving up on LLM recommendations for this entire run. Measured cold start (pod creation
        /// to Ready) was ~6-10 minutes for gemma-4-31b-it-awq. Kept as a defensive fallback even
        /// though the Model CR's minReplicas is now pinned to 1 specifically to avoid this path.
        /// </summary>
        private static readonly TimeSpan COLD_START_BUDGET = TimeSpan.FromMinutes(8);

        private static readonly TimeSpan READINESS_POLL_INTERVAL = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Per-customer LLM call timeout. Generous relative to the ~2s warm latency measured
        /// against real prod order/candidate data - this is not the lever that protects the SLA
        /// (GLOBAL_DEADLINE is), it just bounds how long a single stuck request can hold up the
        /// loop before falling back to the generic reminder for that one customer.
        /// </summary>
        private static readonly TimeSpan LLM_PER_CALL_TIMEOUT = TimeSpan.FromSeconds(15);

        private const string SYSTEM_PROMPT = """
            You are a meal recommendation assistant. Use the user's previous orders to guess their meal preferences. Each previous order may include the customer's own rating (1-5) and comment for that product - weigh these over raw order frequency: a product ordered often but rated low, or with a negative comment, should NOT be treated as a preference and should not be recommended or used to justify a similar recommendation. Candidate products may include a rating: "you rated this" means the customer's own past rating/comment on that exact candidate and should be weighed the same way as previous-order ratings; "avg rating" is the average from all other customers on a candidate the customer has not personally reviewed, and should be used as a general quality signal, not a personal preference signal. Prefer higher-rated candidates when preferences are otherwise similar. Try to guess possible dietary restrictions and use that information when recommending. Use one sentence to explain to the user the reasoning behind your recommendation.
            Give your answer in JSON format with two keys product and reason, no preamble or explanation. The product value must be copied EXACTLY as it appears before any bracketed annotation like "[avg rating ...]" or "[you rated this ...]" - never include the bracketed part in your answer. Respond with raw JSON only. Do not wrap the JSON in markdown code fences or backticks.
            """;

        private class CustomerNotificationMetadata
        {
            public Customer Customer { get; set; }
            public DateTime CurrentTime { get; set; }
            public ICollection<Product> PreviouslyOrderedProducts { get; set; }
        }

        public RemindMeNotificationTask(IDateTimeHelper dateTimeHelper,
            ICustomerService customerService,
            IOrderService orderService,
            ICompanyService companyService,
            KubeAiChatClient kubeAiChatClient,
            IRepository<ProductReview> productReviewRepository,
            ILogger logger,
            IProductService productService,
            PushNotificationService pushNotificationService,
            CatalogSettings catalogSettings)
        {
            _dateTimeHelper = dateTimeHelper;
            _customerService = customerService;
            _orderService = orderService;
            _companyService = companyService;
            _kubeAiChatClient = kubeAiChatClient;
            _productReviewRepository = productReviewRepository;
            _logger = logger;
            _productService = productService;
            _pushNotificationService = pushNotificationService;
            _catalogSettings = catalogSettings;
        }

        /// <summary>
        /// Snaps a minute-of-day value into its 15-minute slot (0..1425), clamped to a valid day range.
        /// </summary>
        private static int SnapToSlot(int minutesOfDay)
        {
            if (minutesOfDay < 0)
                minutesOfDay = 0;
            if (minutesOfDay > 1439)
                minutesOfDay = 1439;

            return minutesOfDay / SLOT_MINUTES * SLOT_MINUTES;
        }

        /// <summary>
        /// The tenant default reminder slot used when a customer has no explicit RemindMeTime.
        /// Derived from CatalogSettings.StartingTimeOfRemindMeTask (interpreted as an hour), defaulting to 10:00.
        /// </summary>
        private int DefaultSlot()
        {
            var hour = _catalogSettings.StartingTimeOfRemindMeTask;
            if (hour <= 0 || hour > 23)
                hour = 10;

            return SnapToSlot(hour * 60);
        }

        private async Task<ICollection<CustomerNotificationMetadata>> GetCustomersToNotify(int loadLastOrders = 40)
        {
            var customersToNotify = new List<CustomerNotificationMetadata>();

            ICollection<Customer> customers =
                await _customerService.GetAllPushNotificationCustomersAsync(isRemindMeNotification: true);

            if (customers.Count == 0)
                return Array.Empty<CustomerNotificationMetadata>();

            var previouslyOrderedProductsByCustomerId =
                await _orderService.GetLastOrderedProductsByCustomerIds(
                    customers.Select(c => c.Id).ToArray(),
                    new[] {OrderStatus.Complete, OrderStatus.Pending, OrderStatus.Processing},
                    loadLastOrders);

            foreach (var customer in customers)
            {
                var customerOrdersData = previouslyOrderedProductsByCustomerId[customer.Id];

                // If customer already ordered for today - we're not going to notify
                if (customerOrdersData.Any() &&
                    customerOrdersData.First().order.ScheduleDate.Date == DateTime.UtcNow.Date)
                {
                    await _logger.InformationAsync($"Customer {customer.Email} already ordered for today, skipping notification",
                        customer: customer);
                    continue;
                }

                // Prefer the customer's COMPANY time zone (resolved via the CompanyCustomer mapping), else fall
                // back to the customer/store time zone. Previously this indexed a companies-by-Id dictionary
                // with the CUSTOMER id, so it never matched and the company zone was silently ignored - every
                // customer fell back to the store default zone.
                var company = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
                var timezoneInfo = string.IsNullOrEmpty(company?.TimeZone)
                    ? await _dateTimeHelper.GetCustomerTimeZoneAsync(customer)
                    : TZConvert.GetTimeZoneInfo(company.TimeZone);

                var customerTime =
                    _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, timezoneInfo);

                // Per-customer reminder-time gate: only notify when the current 15-minute slot (in the
                // customer's time zone) matches ANY of the customer's chosen reminder times (up to 3) -
                // or the tenant default when they have not set any. This buckets customers by their
                // selected slot(s); the task runs every 15 minutes (Hangfire CRON) so a customer with
                // multiple times matches once per configured time per day.
                var currentSlot = SnapToSlot(customerTime.Hour * 60 + customerTime.Minute);
                var remindMeTimes = await _customerService.GetRemindMeTimesAsync(customer);
                var desiredSlots = remindMeTimes.Length > 0
                    ? remindMeTimes.Select(SnapToSlot).ToArray()
                    : new[] { DefaultSlot() };

                if (!desiredSlots.Contains(currentSlot))
                    continue;

                customersToNotify.Add(new CustomerNotificationMetadata()
                {
                    Customer = customer,
                    CurrentTime = customerTime,
                    PreviouslyOrderedProducts = customerOrdersData
                        .Select(o => o.product)
                        .ToList()
                });
            }

            return customersToNotify;
        }

        private class LLMRecommendationResponse
        {
            [JsonPropertyName("product")]
            public string Product { get; set; }
            [JsonPropertyName("reason")]
            public string Reason { get; set; }
        }

        /// <summary>
        /// This customer's own approved reviews for the given products, keyed by ProductId (most
        /// recent review wins if they somehow left more than one for the same product). Used to
        /// prefer a personal opinion over the crowd average - a product ordered often but reviewed
        /// poorly should not be recommended on the strength of its order frequency alone.
        /// </summary>
        private async Task<Dictionary<int, ProductReview>> GetOwnApprovedReviewsByProductIdAsync(
            int customerId, IEnumerable<int> productIds)
        {
            var ids = productIds.Distinct().ToArray();
            if (ids.Length == 0)
                return new Dictionary<int, ProductReview>();

            var reviews = await _productReviewRepository.Table
                .Where(r => r.CustomerId == customerId && r.IsApproved && ids.Contains(r.ProductId))
                .ToListAsync();

            return reviews
                .GroupBy(r => r.ProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedOnUtc).First());
        }

        /// <summary>
        /// Formats one product line for the prompt: the customer's own rating/comment on this
        /// exact product if they left one, else (when <paramref name="includeAvgRatingFallback"/>)
        /// the crowd average from Product's denormalized review totals, else just the bare name.
        /// </summary>
        private static string FormatProductLine(Product product, IReadOnlyDictionary<int, ProductReview> ownReviewsByProductId,
            bool includeAvgRatingFallback)
        {
            if (ownReviewsByProductId.TryGetValue(product.Id, out var ownReview))
            {
                var comment = string.IsNullOrWhiteSpace(ownReview.ReviewText) ? "" : $": \"{ownReview.ReviewText}\"";
                return $"{product.Name} [you rated this {ownReview.Rating}/5{comment}]";
            }

            if (includeAvgRatingFallback && product.ApprovedTotalReviews > 0)
            {
                var avg = Math.Round(product.ApprovedRatingSum / (double)product.ApprovedTotalReviews, 1);
                return $"{product.Name} [avg rating {avg}/5]";
            }

            return product.Name;
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private async Task<Message> GetNotificationMessageForCustomer(
            CustomerNotificationMetadata customerNotificationMetadata,
            Product[] productsToRecommend,
            bool attemptLlmRecommendation)
        {
            var reminderTitle = "Reminder";
            var reminderBody = $"You haven't ordered yet, the time is ticking! Hurry up to secure your lunch for today.";

            if (!attemptLlmRecommendation)
            {
                return new Message()
                {
                    Token = customerNotificationMetadata.Customer.PushToken,
                    Notification = new Notification { Title = reminderTitle, Body = reminderBody },
                    Data = new Dictionary<string, string>() { {"product_quick_order", "/product/[id]"} }
                };
            }

            var previouslyBoughtProductEnglish =
                customerNotificationMetadata.PreviouslyOrderedProducts.Where(p =>
                    p.Name.All(c => char.IsAsciiLetterOrDigit(c) || char.IsWhiteSpace(c))).ToArray();

            var productsToRecommendEnglish =
                productsToRecommend.Where(p =>
                    p.Name.All(c => char.IsAsciiLetterOrDigit(c) || char.IsWhiteSpace(c))).ToArray();

            try
            {
                var relevantProductIds = previouslyBoughtProductEnglish.Select(p => p.Id)
                    .Concat(productsToRecommendEnglish.Select(p => p.Id));
                var ownReviewsByProductId = await GetOwnApprovedReviewsByProductIdAsync(
                    customerNotificationMetadata.Customer.Id, relevantProductIds);

                var previouslyBoughtProductEnglishString = previouslyBoughtProductEnglish.Any()
                    ? string.Join('\n', previouslyBoughtProductEnglish.Select(p =>
                        FormatProductLine(p, ownReviewsByProductId, includeAvgRatingFallback: false)))
                    : "No previous orders";

                var productsToRecommendEnglishString = string.Join('\n', productsToRecommendEnglish.Select(p =>
                    FormatProductLine(p, ownReviewsByProductId, includeAvgRatingFallback: true)));

                var userPrompt = $"""
                                  Previous orders: {previouslyBoughtProductEnglishString}
                                  Recommend me one of the following products: {productsToRecommendEnglishString}
                                  """;

                using var cts = new CancellationTokenSource(LLM_PER_CALL_TIMEOUT);
                var rawContent = await _kubeAiChatClient.GetChatCompletionAsync(
                    LLM_MODEL, SYSTEM_PROMPT, userPrompt, LLM_PER_CALL_TIMEOUT, cts.Token);

                var recommendation = JsonSerializer.Deserialize<LLMRecommendationResponse>(rawContent);
                reminderBody = $"""
                                Don't forget to order before it's too late!

                                Recommended for you: {recommendation.Product}

                                Reason: {recommendation.Reason}
                                """;
            }
            catch (Exception e)
            {
                await _logger.InformationAsync("Something gone wrong while querying the recommendation model", e);
            }

            return new Message()
            {
                Token = customerNotificationMetadata.Customer.PushToken,
                Notification = new Notification
                {
                    Title = reminderTitle,
                    Body = reminderBody
                },
                Data = new Dictionary<string, string>() { {"product_quick_order", "/product/[id]"} }
            };
        }

        /// <summary>
        /// Waits, once, for the LLM to be reachable before processing any customer - rather than
        /// letting every customer's call independently discover a cold model via its own timeout.
        /// Bounded by COLD_START_BUDGET; returns false (never throws) if it never becomes ready.
        /// </summary>
        private async Task<bool> WaitForModelReadyAsync(DateTime runStartUtc)
        {
            var coldStartDeadlineUtc = runStartUtc + COLD_START_BUDGET;

            while (true)
            {
                if (await _kubeAiChatClient.IsReadyAsync(LLM_MODEL, LLM_PER_CALL_TIMEOUT))
                    return true;

                var remaining = coldStartDeadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    await _logger.WarningAsync(
                        $"RemindMe: {LLM_MODEL} was not ready within the {COLD_START_BUDGET.TotalMinutes:0}-minute " +
                        "cold-start budget; sending generic reminders (no recommendation) for this run");
                    return false;
                }

                await System.Threading.Tasks.Task.Delay(remaining < READINESS_POLL_INTERVAL ? remaining : READINESS_POLL_INTERVAL);
            }
        }

        /// <summary>
        /// Executes a task
        /// </summary>
        public async System.Threading.Tasks.Task ExecuteAsync()
        {
            var runStartUtc = DateTime.UtcNow;
            var globalDeadlineUtc = runStartUtc + GLOBAL_DEADLINE;

            // Runs every 15 minutes (Hangfire CRON "*/15 * * * *"). GetCustomersToNotify() filters to the
            // customers whose chosen 15-minute reminder slot matches the current time in their own time zone,
            // so this run only processes the current slot's bucket rather than every opted-in customer.
            var expensiveProducts = await _productService.SearchProductsAsync(orderBy: ProductSortingEnum.PriceDesc);

            var productsToRecommend = expensiveProducts.Take(70)
                .Concat(expensiveProducts.Reverse().Skip(Random.Shared.Next(0, expensiveProducts.Count - 70 - 30)).Take(30))
                .DistinctBy(p => p.Id)
                .ToArray();

            var customerNotificationMetadata = await GetCustomersToNotify();

            var llmReady = customerNotificationMetadata.Count > 0 && await WaitForModelReadyAsync(runStartUtc);

            foreach (var notificationMetadata in customerNotificationMetadata)
            {
                try
                {
                    // Once there isn't enough of the global budget left for even one more LLM call
                    // to finish, stop attempting it - remaining customers still get the generic
                    // reminder, just without a recommendation, rather than risking the whole run
                    // (and their notification) blowing past the SLA.
                    var attemptLlm = llmReady && DateTime.UtcNow + LLM_PER_CALL_TIMEOUT < globalDeadlineUtc;

                    var message = await GetNotificationMessageForCustomer(notificationMetadata, productsToRecommend, attemptLlm);

                    await _pushNotificationService.SendNotificationAsync(notificationMetadata.Customer,
                        NotificationType.RemindMe,
                        message.Notification.Title,
                        message.Notification.Body,
                        message.Data);

                    await _logger.InformationAsync($"Reminder sent to user: {message.Notification.Body}",
                        customer: notificationMetadata.Customer);
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync(
                        $"Failed to send notification to customer {notificationMetadata.Customer.Email}", e, notificationMetadata.Customer);
                }
            }
        }
    }
}
