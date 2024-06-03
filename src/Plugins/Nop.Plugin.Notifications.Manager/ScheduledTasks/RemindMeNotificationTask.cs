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
        private readonly CatalogSettings _catalogSettings;
        private readonly ISettingService _settingService;
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly ILocalizationService _localizationService;
        private readonly ICompanyService _companyService;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly IOllamaApiClient _ollamaApiClient;
        private readonly ILogger _logger;
        private readonly FirebaseApp _firebaseApp;

        private class CustomerNotificationMetadata
        {
            public Customer Customer { get; set; }
            public DateTime CurrentTime { get; set; }
            public ICollection<Product> PreviouslyOrderedProducts { get; set; }
        }
        
        public RemindMeNotificationTask(IDateTimeHelper dateTimeHelper,
            CatalogSettings catalogSettings,
            ICustomerService customerService,
            ISettingService settingService,
            IOrderService orderService,
            ILocalizationService localizationService,
            ICompanyService companyService,
            ICustomerActivityService customerActivityService,
            IOllamaApiClient ollamaApiClient, 
            ILogger logger, 
            IProductService productService, 
            FirebaseApp firebaseApp)
        {
            _dateTimeHelper = dateTimeHelper;
            _catalogSettings = catalogSettings;
            _customerService = customerService;
            _settingService = settingService;
            _orderService = orderService;
            _localizationService = localizationService;
            _companyService = companyService;
            _customerActivityService = customerActivityService;
            _ollamaApiClient = ollamaApiClient;
            _logger = logger;
            _productService = productService;
            _firebaseApp = firebaseApp;
        }

        private async Task<ICollection<CustomerNotificationMetadata>> GetCustomersToNotify(int loadLastOrders = 10)
        {
            var customersToNotify = new List<CustomerNotificationMetadata>();
            
            var companies = (await _companyService.GetAllCompaniesAsync())
                .ToDictionary(c => c.Id);

            ICollection<Customer> customers =
                await _customerService.GetAllPushNotificationCustomersAsync(isRemindMeNotification: true);
            
            // TODO: only for debugging
            customers = customers.Where(c => c.Id == 57061 || c.Id == 326739).ToList();

            var previouslyOrderedProductsByCustomerId =
                await _orderService.GetLastOrderedProductsByCustomerIds(
                    customers.Select(c => c.Id).ToArray(), 
                    new[] {OrderStatus.Complete, OrderStatus.Pending, OrderStatus.Processing},
                    loadLastOrders,
                    // Exclude orders on today
                    DateTime.UtcNow);

            if (customers.Count == 0)
                return Array.Empty<CustomerNotificationMetadata>();
            
            foreach (var customer in customers)
            {
                var timezoneInfo = !companies.TryGetValue(customer.Id, out var company) || company.TimeZone == null
                    ? await _dateTimeHelper.GetCustomerTimeZoneAsync(customer)
                    : TZConvert.GetTimeZoneInfo(company.TimeZone);

                var customerTime =
                    _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, timezoneInfo);

                customersToNotify.Add(new CustomerNotificationMetadata()
                {
                    Customer = customer,
                    CurrentTime = customerTime,
                    PreviouslyOrderedProducts = previouslyOrderedProductsByCustomerId[customer.Id]
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
                        Model = "llama3:instruct",
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
            // var startingHour = await _settingService.GetSettingByKeyAsync("catalogSettings.StartingTimeOfRemindMeTask", 
            //     10);

            var expensiveProducts = await _productService.SearchProductsAsync(orderBy: ProductSortingEnum.PriceDesc);
            
            var productsToRecommend = expensiveProducts.Take(70)
                .Concat(expensiveProducts.Reverse().Skip(Random.Shared.Next(0, expensiveProducts.Count - 70 - 30)).Take(30))
                .DistinctBy(p => p.Id)
                .ToArray();
            
            var customerNotificationMetadata = await GetCustomersToNotify();
            
            foreach (var notificationMetadata in customerNotificationMetadata)
            {
                // if (notificationMetadata.CurrentTime.Hour == startingHour)
                // {
                    await _customerActivityService.InsertActivityAsync("User Reminder",
                        $"Remind user {notificationMetadata.Customer.Email}", notificationMetadata.Customer);
                    
                    try
                    {
                        var message = await GetNotificationMessageForCustomer(notificationMetadata, productsToRecommend);
                        await FirebaseMessaging.GetMessaging(_firebaseApp).SendAsync(message);
                    }
                    catch (Exception e)
                    {
                        await _logger.ErrorAsync(
                            $"Failed to send notification to customer {notificationMetadata.Customer.Email}", e);
                    }
                //}
            }
        }
    }
}
