using System;
using System.Collections.Generic;
using Expo.Server.Client;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Services.Common.PushApiTask;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Tasks;

namespace Nop.Services.Common
{
    /// <summary>
    /// Represents a task for sending reminding notification to customer
    /// </summary>
    public partial class RemindMeNotificationTask : IScheduleTask
    {
        #region Fields

        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly CatalogSettings _catalogSettings;
        private readonly ISettingService _settingService;
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public RemindMeNotificationTask(IDateTimeHelper dateTimeHelper,
            CatalogSettings catalogSettings,
            ICustomerService customerService,
            ISettingService settingService,
            IOrderService orderService,
            ILocalizationService localizationService)
        {
            _dateTimeHelper = dateTimeHelper;
            _catalogSettings = catalogSettings;
            _customerService = customerService;
            _settingService = settingService;
            _orderService = orderService;
            _localizationService = localizationService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Executes a task
        /// </summary>
        public async System.Threading.Tasks.Task ExecuteAsync()
        {
            var startingHour = await _settingService.GetSettingByKeyAsync<int>("catalogSettings.StartingTimeOfRemindMeTask");
            if (startingHour == 0)
                startingHour = 11;
                
            var customers = await _customerService.GetAllPushNotificationCustomersAsync(isRemindMeNotification: true);
            if (customers.Count > 0)
            {
                DateTime currentDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day);
                var osIds = new List<int> { (int)OrderStatus.Complete, (int)OrderStatus.Pending, (int)OrderStatus.Processing };
                foreach (var customer in customers)
                {
                    var customerTime = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, await _dateTimeHelper.GetCustomerTimeZoneAsync(customer));
                    if (customerTime.Hour == startingHour)
                    {
                        var order = await _orderService.SearchOrdersAsync(customerId: customer.Id, createdToUtc: currentDate, osIds: osIds);
                        if (order.Count == 0)
                        {
                            if (!string.IsNullOrEmpty(customer.PushToken))
                            {
                                var expoSDKClient = new PushApiTaskClient();
                                var pushTicketReq = new PushApiTaskTicketRequest()
                                {
                                    PushTo = new List<string>() { customer.PushToken },
                                    PushTitle = await _localizationService.GetResourceAsync("RemindMeNotificationTask.Title"),
                                    PushBody = await _localizationService.GetResourceAsync("RemindMeNotificationTask.Body")
                                };
                                var result = await expoSDKClient.PushSendAsync(pushTicketReq);
                            }
                        }
                    }
                }
            }
        }
        
        #endregion
    }
}
