using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Services.Authentication;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Customer
{
    [Produces("application/json")]
    [Route("api/customer")]
    [AuthorizeAttribute]
    public class CustomerApiController : BaseApiController
    {
        #region Fields
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly IAddressService _addressService;
        private readonly MediaSettings _mediaSettings;
        private readonly IPictureService _pictureService;
        private readonly ICompanyService _companyService;

        #endregion

        #region Ctor

        public CustomerApiController(
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IWorkContext workContext,
            IAddressService addressService,
            MediaSettings mediaSettings,
            IPictureService pictureService,
            ICompanyService companyService)
        {
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _localizationService = localizationService;
            _workContext = workContext;
            _addressService = addressService;
            _mediaSettings = mediaSettings;
            _pictureService = pictureService;
            _companyService = companyService;
        }

        #endregion

        #region Methods

        public class CompanyDetailsModel
        {
            public CompanyDetailsModel()
            {
                Adresses = new List<Address>();
            }
            public IList<Address> Adresses { get; set; }
            public decimal AmoutLimit { get; set; }
            public string CompanyName { get; set; }
        }

        [HttpGet("customer-details")]
        public async Task<IActionResult> CustomerDetails()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Customer.Not.Found") });

            var shippingAddress = customer.ShippingAddressId.HasValue ? await _addressService.GetAddressByIdAsync(customer.ShippingAddressId.Value) : null;

            var firstName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.FirstNameAttribute);
            var lastName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.LastNameAttribute);

            return Ok(new
            {
                success = true,
                message = await _localizationService.GetResourceAsync("Customer.Login.Successfully"),
                pushToken = customer.PushToken,
                shippingAddress,
                firstName,
                customer.Id,
                customer.Email,
                lastName,
                RemindMeNotification = customer.RemindMeNotification,
                RateReminderNotification = customer.RateReminderNotification,
                OrderStatusNotification = customer.OrderStatusNotification,
                avatar = await _pictureService.GetPictureUrlAsync(await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.AvatarPictureIdAttribute), _mediaSettings.AvatarPictureSize, true)
            });
        }

        [HttpGet("set-deliveryaddress/{addressId}")]
        public async Task<IActionResult> SetDeilverAddress(int addressId)
        {
            var address = await _addressService.GetAddressByIdAsync(addressId);
            if (address == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("address.notfound") });

            //try to get an customer with the order id
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("customer.NotFound") });

            customer.BillingAddressId = addressId;
            customer.ShippingAddressId = addressId;
            await _customerService.UpdateCustomerAsync(customer);
            
            return Ok(new { success = true, message = await _localizationService.GetResourceAsync("DeliveryAddressSet.Successfully") });
        }

        [HttpGet("company-details")]
        public async Task<IActionResult> GetCompanyDetails()
        {
            var companyDetails = new CompanyDetailsModel();
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);

            if (company == null)
            {
                return Ok(new
                {
                    success = false, message = await _localizationService.GetResourceAsync("Company.NotFound")
                });
            }
            
            companyDetails.CompanyName = company.Name;
            companyDetails.AmoutLimit = company.AmountLimit;
            companyDetails.Adresses = await _customerService.GetAddressesByCustomerIdAsync(currentCustomer.Id);
            
            return Ok(new { success = true, companyDetails });
        }

        [HttpGet("update-customer-pushtoken/{pushToken}")]
        public virtual async Task<IActionResult> SetCustomerPushToken(string pushToken)
        {
            if (!string.IsNullOrWhiteSpace(pushToken))
            {
                var customer = await _workContext.GetCurrentCustomerAsync();
                if (customer == null)
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Customer.Not.Found") });

                customer.PushToken = pushToken;
                await _customerService.UpdateCustomerAsync(customer);
                return Ok(new { success = true, message = await _localizationService.GetResourceAsync("Account.Customer.PushTokenUpdated") });
            }
            return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Customer.PushTokenNotFound") });
        }

        #endregion
    }
}
