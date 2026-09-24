using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Media;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Customer
{
    /// <summary>
    /// mobile-v2's own account-details/notification-settings surface -
    /// api/v2/customer, versioned alongside api/v2/catalog/cart/checkout/order.
    /// Mirrors the real v1 backend (CustomerApiController.CustomerDetails +
    /// PushNotificationApiController.SavePushNotification) against the same real
    /// services rather than proxying v1's own endpoints.
    ///
    /// There is no real endpoint anywhere in this backend to edit a customer's
    /// name/email/password (confirmed - v1's CustomerApiController/
    /// AccountApiController have no such action either), so "details" here is
    /// deliberately read-only; only notification preferences are genuinely
    /// editable, matching what the real backend actually supports.
    /// </summary>
    [Produces("application/json")]
    [Route("api/v2/customer")]
    [Authorize]
    public class CustomerV2ApiController(
        IWorkContext workContext,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        IPictureService pictureService,
        IAddressService addressService,
        MediaSettings mediaSettings)
        : BaseApiController
    {
        /// <summary>15-minute reminder slot granularity (matches PushNotificationApiController).</summary>
        private const int SlotMinutes = 15;

        public class CustomerDetailsV2Model
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string Email { get; set; }
            public string AvatarUrl { get; set; }
            public string DeliveryAddress { get; set; }
            public bool RemindMeNotification { get; set; }
            public bool RateReminderNotification { get; set; }
            public bool OrderStatusNotification { get; set; }
            // "HH:mm" strings, same real shape PushNotificationApiController reads/writes.
            public IList<string> RemindMeTimes { get; set; } = new List<string>();
        }

        public class NotificationSettingsV2Model
        {
            public bool RemindMeNotification { get; set; }
            public bool RateReminderNotification { get; set; }
            public bool OrderStatusNotification { get; set; }
            public IList<string> RemindMeTimes { get; set; } = new List<string>();
        }

        [HttpGet("details")]
        public async Task<IActionResult> GetDetails()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            return Ok(await BuildDetailsAsync(customer));
        }

        [HttpPost("notification-settings")]
        public async Task<IActionResult> SaveNotificationSettings([FromBody] NotificationSettingsV2Model model)
        {
            var customer = await workContext.GetCurrentCustomerAsync();

            customer.RemindMeNotification = model?.RemindMeNotification ?? false;
            customer.RateReminderNotification = model?.RateReminderNotification ?? false;
            customer.OrderStatusNotification = model?.OrderStatusNotification ?? false;
            await customerService.UpdateCustomerAsync(customer);

            var remindMeTimes = (model?.RemindMeTimes ?? new List<string>())
                .Select(ParseRemindMeTime)
                .Where(time => time.HasValue)
                .Select(time => time.Value)
                .ToArray();
            await customerService.SetRemindMeTimesAsync(customer, remindMeTimes);

            return Ok(await BuildDetailsAsync(customer));
        }

        private async Task<CustomerDetailsV2Model> BuildDetailsAsync(Nop.Core.Domain.Customers.Customer customer)
        {
            var firstName = await genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.FirstNameAttribute);
            var lastName = await genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.LastNameAttribute);
            var avatarPictureId = await genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.AvatarPictureIdAttribute);
            var remindMeTimes = await customerService.GetRemindMeTimesAsync(customer);

            string deliveryAddress = null;
            if (customer.ShippingAddressId.HasValue)
            {
                var address = await addressService.GetAddressByIdAsync(customer.ShippingAddressId.Value);
                if (address != null)
                {
                    deliveryAddress = string.Join(", ", new[] { address.Address1, address.Address2, address.City }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));
                }
            }

            return new CustomerDetailsV2Model
            {
                Id = customer.Id,
                FirstName = firstName,
                LastName = lastName,
                Email = customer.Email,
                AvatarUrl = await pictureService.GetPictureUrlAsync(avatarPictureId, mediaSettings.AvatarPictureSize, true),
                DeliveryAddress = deliveryAddress,
                RemindMeNotification = customer.RemindMeNotification,
                RateReminderNotification = customer.RateReminderNotification,
                OrderStatusNotification = customer.OrderStatusNotification,
                RemindMeTimes = remindMeTimes
                    .Select(minutes => TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm", CultureInfo.InvariantCulture))
                    .ToList()
            };
        }

        /// <summary>
        /// Parses an "HH:mm" reminder time into minutes-after-midnight, snapped to a
        /// 15-minute slot. Returns null for null/empty/invalid input. Same logic as
        /// PushNotificationApiController.ParseRemindMeTime.
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

            return minutes / SlotMinutes * SlotMinutes;
        }
    }
}
