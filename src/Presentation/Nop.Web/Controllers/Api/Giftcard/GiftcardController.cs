using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Payments;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.Api.Giftcard;

namespace Nop.Web.Controllers.Giftcard
{
    [Produces("application/json")]
    [Route("api/giftcard")]
    [Authorize]
    public class GiftcardController : BaseApiController
    {
        private readonly IGiftCardService _giftCard;
        private readonly ICustomerService _customer;
        private readonly IOrderService _order;
        private readonly IWorkContext _workContext;
        private readonly ICompanyService _company;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly ICustomNumberFormatter _numberFormatter;
        private readonly IPermissionService _permission;
        private readonly IStoreContext _storeContext;

        [HttpPost("bookforcustomer")]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(BookResponse), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> BookForCustomerAsync([FromBody]BookRequest bookRequest)
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var currentCompany = await _company.GetCompanyByCustomerIdAsync(currentCustomer.Id);
            
            if (currentCompany == null || !ModelState.IsValid)
                return BadRequest(new ErrorMessage("Invalid parameters"));

            if (!await _permission.AuthorizeAsync("ManageCompanyGiftCards"))
                return Unauthorized(new ErrorMessage("Insufficient of rights"));

            var customer = await _customer.GetCustomerByEmailAsync(bookRequest.CustomerEmail);
            if (customer == null ||
                currentCompany.Id != (await _company.GetCompanyByCustomerIdAsync(customer.Id))?.Id)
            {
                return BadRequest(new ErrorMessage("Customer not found"));
            }

            var orders = await _order.SearchOrdersAsync(customerId: customer.Id,
                scheduleDateTime: bookRequest.BookingDate);
            if (orders.Any(o => o.OrderStatus != OrderStatus.Cancelled))
                return Conflict(new ErrorMessage("Customer has orders on specified date"));

            var (lastBillingAddressId, lastShippingAddressId) = await GetCustomerLastUsedAddressIdAsync(customer);
            var order = new Order()
            {
                CompanyId = currentCompany.Id,
                CurrencyRate = 1,
                CustomerId = customer.Id,
                OrderGuid = Guid.NewGuid(),
                OrderStatus = OrderStatus.Complete,
                OrderTotal = currentCompany.AmountLimit,
                OrderSubtotalExclTax = currentCompany.AmountLimit,
                OrderSubtotalInclTax = currentCompany.AmountLimit,
                PaymentStatus = PaymentStatus.Pending,
                ScheduleDate = bookRequest.BookingDate,
                ScheduleDateTime = bookRequest.BookingDate,
                ShippingStatus = ShippingStatus.ShippingNotRequired,
                StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
                BillingAddressId = lastBillingAddressId,
                ShippingAddressId = lastShippingAddressId, 
                CreatedOnUtc = DateTime.UtcNow,
                CustomerCurrencyCode = (await _workContext.GetWorkingCurrencyAsync()).CurrencyCode,
                DeliverySlot = -customer.Id,
                PaymentMethodSystemName = await GetPaymentMethodSystemNameAsync(),
                CustomOrderNumber = string.Empty
            };
            
            await _order.InsertOrderAsync(order);
            order.CustomOrderNumber = _numberFormatter.GenerateOrderCustomNumber(order);
            await _order.UpdateOrderAsync(order);

            var nonActivatedGiftCards = 
                await _giftCard.GetAllGiftCardsAsync(isGiftCardActivated: false);
            if (!nonActivatedGiftCards.Any())
                return NotFound(new ErrorMessage("No available gift card found"));

            var giftCard = nonActivatedGiftCards.First();
            giftCard.IsGiftCardActivated = true;
            await _giftCard.UpdateGiftCardAsync(giftCard);
            
            await _giftCard.InsertGiftCardUsageHistoryAsync(new GiftCardUsageHistory()
            {
                CreatedOnUtc = DateTime.UtcNow,
                GiftCardId = giftCard.Id,
                UsedValue = giftCard.Amount,
                UsedWithOrderId = order.Id
            });

            return Ok(new BookResponse()
            {
                Amount = giftCard.Amount,
                CouponCode = giftCard.GiftCardCouponCode
            });
        }

        private async Task<string> GetPaymentMethodSystemNameAsync()
        {
            var paymentMethods = await _paymentPluginManager.LoadActivePluginsAsyncAsync();
            return paymentMethods.First().ToPluginModel<PaymentMethodModel>().SystemName;
        }
        
        private async Task<(int billingAddress, int shippingAddress)> GetCustomerLastUsedAddressIdAsync(Customer customer)
        {
            if(customer.BillingAddressId.HasValue && customer.ShippingAddressId.HasValue)
                return (customer.BillingAddressId.Value!, customer.ShippingAddressId.Value!);

            var lastOrder = (await _order.SearchOrdersAsync(customerId: customer.Id, 
                pageSize: 1, 
                sortByDeliveryDate: true)).First();
            
            return (lastOrder.BillingAddressId, lastOrder.ShippingAddressId.Value!);
        }
        
        public GiftcardController(IGiftCardService giftCard, 
            ICustomerService customer, 
            IOrderService order, 
            IWorkContext workContext, 
            ICompanyService company, 
            IPaymentPluginManager pluginManager, 
            ICustomNumberFormatter numberFormatter, IPermissionService permission, IStoreContext storeContext)
        {
            _giftCard = giftCard;
            _customer = customer;
            _order = order;
            _workContext = workContext;
            _company = company;
            _paymentPluginManager = pluginManager;
            _numberFormatter = numberFormatter;
            _permission = permission;
            _storeContext = storeContext;
        }
    }
}