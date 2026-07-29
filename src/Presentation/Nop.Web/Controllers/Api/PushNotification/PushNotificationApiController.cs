using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.PushNotification
{
    [Produces("application/json")]
    [Route("api/push-notification")]
    [Authorize]
    public class PushNotificationApiController : BaseApiController
    {
        #region Fields

        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly ILocalizationService _localizationService;
        #endregion

        #region Ctor

        public PushNotificationApiController(
            IWorkContext workContext,
            ICustomerService customerService,
            ILocalizationService localizationService)
        {
            _workContext = workContext;
            _customerService = customerService;
            _localizationService = localizationService;
        }

        #endregion

        #region Nested Class

        public class PushNotifcationModel
        {
            public bool OrderStatusNotification { get; set; }
            public bool RemindMeNotification { get; set; }
            public bool RateReminderNotification { get; set; }

            /// <summary>
            /// Preferred order-reminder times as "HH:mm" strings (24-hour, snapped to 15-minute
            /// slots, max 3). Null/empty clears the preference so the tenant default applies.
            /// </summary>
            public List<string> RemindMeTimes { get; set; }
        }

        /// <summary>
        /// 15-minute reminder slot granularity (matches the mobile picker and the dispatcher CRON).
        /// </summary>
        private const int SLOT_MINUTES = 15;

        /// <summary>
        /// Parses an "HH:mm" reminder time into minutes-after-midnight, snapped to a 15-minute slot.
        /// Returns null for null/empty/invalid input (meaning "no preference").
        /// </summary>
        private static int? ParseRemindMeTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeOfDay))
                return null;

            var minutes = (int)timeOfDay.TotalMinutes;
            if (minutes < 0)
                minutes = 0;
            if (minutes > 1439)
                minutes = 1439;

            return minutes / SLOT_MINUTES * SLOT_MINUTES;
        }

        #endregion

        #region Push Notification

        [HttpPost("save-notification-settings")]
        public async Task<IActionResult> SavePushNotification([FromBody] PushNotifcationModel model)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Customer.Not.Found") });

            customer.OrderStatusNotification = model.OrderStatusNotification;
            customer.RateReminderNotification = model.RateReminderNotification;
            customer.RemindMeNotification= model.RemindMeNotification;
            await _customerService.UpdateCustomerAsync(customer);

            var remindMeTimes = (model.RemindMeTimes ?? new List<string>())
                .Select(ParseRemindMeTime)
                .Where(time => time.HasValue)
                .Select(time => time.Value)
                .ToArray();
            await _customerService.SetRemindMeTimesAsync(customer, remindMeTimes);

            return Ok(new { success = true, message = await _localizationService.GetResourceAsync("Customer.Notification.Settings.Updated") });
        }

        #endregion
    }
}
