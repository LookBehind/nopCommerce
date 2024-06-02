using System;
using System.Collections.Generic;
using System.Linq;
using Expo.Server.Client;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Services.Common.PushApiTask;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Tasks;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks
{
    /// <summary>
    /// Represents a task for sending reminding notification to customer
    /// </summary>
    public class RateRemainderNotificationTask : IScheduleTask
    {
        #region Fields

        private readonly CatalogSettings _catalogSettings;
        private readonly ISettingService _settingService;
        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public RateRemainderNotificationTask(CatalogSettings catalogSettings,
            ICustomerService customerService,
            ISettingService settingService,
            IOrderService orderService,
            ILocalizationService localizationService)
        {
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
            var rateRemainderCustomers = await _customerService.GetAllPushNotificationCustomersAsync(isRateReminderNotification: true);
            if (rateRemainderCustomers.Count > 0)
            {
                //DateTime currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
                var osIds = new List<int> { (int)OrderStatus.Complete };
                foreach (var rateRemainderCustomer in rateRemainderCustomers)
                {
                    var customerOrder = (await _orderService.SearchOrdersAsync(customerId: rateRemainderCustomer.Id, osIds: osIds, sendRateNotification: true, schedulDate: DateTime.UtcNow))
                        .OrderByDescending(o => o.ScheduleDate).FirstOrDefault();
                    if (customerOrder != null)
                    {
                        TimeSpan diff = DateTime.UtcNow.Subtract(customerOrder.ScheduleDate);
                        if (diff.Hours >= 1 && DateTime.UtcNow.Date == customerOrder.ScheduleDate.Date)
                        {
                            if (!string.IsNullOrEmpty(rateRemainderCustomer.PushToken))
                            {
                                var expoSDKClient = new PushApiTaskClient();
                                var pushTicketReq = new PushApiTaskTicketRequest()
                                {
                                    PushTo = new List<string>() { rateRemainderCustomer.PushToken },
                                    PushTitle = await _localizationService.GetResourceAsync("RateRemainderNotificationTask.Title"),
                                    PushBody = await _localizationService.GetResourceAsync("RateRemainderNotificationTask.Body"),
                                    PushData = new { customerOrder.Id }
                                };
                                var result = await expoSDKClient.PushSendAsync(pushTicketReq);

                                customerOrder.RateNotificationSend = true;
                                await _orderService.UpdateOrderAsync(customerOrder);
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}