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

            return Ok(new { success = true, message = await _localizationService.GetResourceAsync("Customer.Notification.Settings.Updated") });
        }

        #endregion
    }
}
