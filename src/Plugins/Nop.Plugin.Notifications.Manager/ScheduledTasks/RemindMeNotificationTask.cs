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
using OllamaSharp;
using OllamaSharp.Models.Chat;
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
        private readonly IOllamaApiClient _ollamaApiClient;
        private readonly ILogger _logger;
        private readonly PushNotificationService _pushNotificationService;
        private readonly CatalogSettings _catalogSettings;

        /// <summary>
        /// Reminder-time slot granularity in minutes. The mobile picker offers 15-minute slots and this task
        /// is registered as a Hangfire recurring job on the matching "*/15 * * * *" CRON.
        /// </summary>
        private const int SLOT_MINUTES = 15;

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
            IOllamaApiClient ollamaApiClient,
            ILogger logger,
            IProductService productService,
            PushNotificationService pushNotificationService,
            CatalogSettings catalogSettings)
        {
            _dateTimeHelper = dateTimeHelper;
            _customerService = customerService;
            _orderService = orderService;
            _companyService = companyService;
            _ollamaApiClient = ollamaApiClient;
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
                // customer's time zone) matches the customer's chosen reminder time - or the tenant default
                // when they have not set one. This buckets customers by their selected slot; the task runs
                // every 15 minutes (Hangfire CRON) so each customer matches exactly once per day.
                var currentSlot = SnapToSlot(customerTime.Hour * 60 + customerTime.Minute);
                var desiredSlot = customer.RemindMeTime.HasValue
                    ? SnapToSlot(customer.RemindMeTime.Value)
                    : DefaultSlot();

                if (currentSlot != desiredSlot)
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
        
        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private async Task<Message> GetNotificationMessageForCustomer(
            CustomerNotificationMetadata customerNotificationMetadata, 
            Product[] productsToRecommend)
        {
            var reminderTitle = "Reminder";
            var reminderBody = $"You haven't ordered yet, the time is ticking! Hurry up to secure your lunch for today.";

            var previouslyBoughtProductEnglish =
                customerNotificationMetadata.PreviouslyOrderedProducts.Where(p => 
                    p.Name.All(c => char.IsAsciiLetterOrDigit(c) || char.IsWhiteSpace(c)));

            var previouslyBoughtProductEnglishString = 
                previouslyBoughtProductEnglish.Any() ?
                string.Join('\n', previouslyBoughtProductEnglish.Select(p => p.Name))
                :
                "No previous orders";
            
            var productsToRecommendEnglish =
                productsToRecommend.Where(p => 
                    p.Name.All(c => char.IsAsciiLetterOrDigit(c) || char.IsWhiteSpace(c)));

            var productsToRecommendEnglishString = string.Join('\n', productsToRecommendEnglish.Select(p => p.Name));
            
            try
            {
                using var cts = new CancellationTokenSource(300000);

                var messages = await _ollamaApiClient.SendChat(new ChatRequest()
                    {
                        Model = "llama-3.1-8b-instruct",
                        Stream = false,
                        KeepAlive = "30m",
                        Messages = new List<OllamaSharp.Models.Chat.Message>()
                        {
                            new OllamaSharp.Models.Chat.Message(ChatRole.System,
                                """
                                You are a meal recommendation assistant. 
                                Use the user's previous orders to guess their meal preferences. 
                                Try to guess possible dietary restrictions and use that information when recommending. 
                                Use one sentence to explain to the user the reasoning behind your recommendation.
                                Give your answer in JSON format with two keys product and reason, no preamble or explanation.
                                """),
                            new OllamaSharp.Models.Chat.Message(ChatRole.User, 
                                $"""
                                Previous orders: {previouslyBoughtProductEnglishString}
                                Recommend me one of the following products: {productsToRecommendEnglishString}
                                """)
                        }
                    },
                    rs =>
                    {

                    }, cts.Token);

                var recommendation = JsonSerializer.Deserialize<LLMRecommendationResponse>(messages.Last().Content);
                reminderBody = $"""
                                Don't forget to order before it's too late! 
                                
                                Recommended for you: {recommendation.Product}
                                
                                Reason: {recommendation.Reason}
                                """;
            }
            catch (Exception e)
            {
                await _logger.InformationAsync("Something gone wrong while querying ollama", e);
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
        /// Executes a task
        /// </summary>
        public async System.Threading.Tasks.Task ExecuteAsync()
        {
            // Runs every 15 minutes (Hangfire CRON "*/15 * * * *"). GetCustomersToNotify() filters to the
            // customers whose chosen 15-minute reminder slot matches the current time in their own time zone,
            // so this run only processes the current slot's bucket rather than every opted-in customer.
            var expensiveProducts = await _productService.SearchProductsAsync(orderBy: ProductSortingEnum.PriceDesc);
            
            var productsToRecommend = expensiveProducts.Take(70)
                .Concat(expensiveProducts.Reverse().Skip(Random.Shared.Next(0, expensiveProducts.Count - 70 - 30)).Take(30))
                .DistinctBy(p => p.Id)
                .ToArray();
            
            var customerNotificationMetadata = await GetCustomersToNotify();
            
            foreach (var notificationMetadata in customerNotificationMetadata)
            {
                try
                {
                    var message = await GetNotificationMessageForCustomer(notificationMetadata, productsToRecommend);
                    
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
