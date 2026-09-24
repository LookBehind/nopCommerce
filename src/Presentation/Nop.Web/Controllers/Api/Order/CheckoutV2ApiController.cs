using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.Api.Order;
using TimeZoneConverter;

namespace Nop.Web.Controllers.Api.Order
{
    /// <summary>
    /// mobile-v2's real checkout/order-placement surface - api/v2/checkout, versioned
    /// alongside api/v2/catalog and api/v2/cart. Mirrors the real v1 mobile backend's
    /// order-placement flow (OrderApiController.OrderConfirmation + CustomerApiController's
    /// address actions) rather than inventing a parallel concept, with two simplifications
    /// v1 doesn't have:
    ///
    /// 1. No "check-products" cart-rebuild step - v1 needs it because its cart is otherwise
    ///    client-tracked; api/v2/cart (CartV2ApiController) is already a real persistent
    ///    server cart, so PlaceOrderAsync is called directly against whatever's already there.
    ///
    /// 2. ScheduleDate round-trips exactly: GetSummary emits each available slot as an
    ///    unambiguous "yyyy-MM-ddTHH:mm:ss" local string, and the client is expected to echo
    ///    one back verbatim to PlaceOrder - unlike v1's ConvertCustomerLocalTimeToUTCAsync,
    ///    which has to guess-and-fix a raw client-typed/converted string, this control's
    ///    entire contract is owned end-to-end here, so no such fixup is needed.
    ///
    /// The mock ConfirmOrderSheet.tsx this replaces had a per-address `isCompany` flag driving
    /// an "out of company benefit" banner - confirmed (via investigation) to have no real
    /// backend concept behind it (no field on Address or Company distinguishes "company" vs
    /// "personal" at checkout time). The real, load-bearing warning is purely
    /// cart-total-vs-company-allowance (BuildCheckoutWarningAsync below mirrors
    /// OrderApiController's private method of the same name, which the real v1 app's
    /// OrderSummaryCards.js explicitly treats as server-computed, not re-derived client-side).
    [Produces("application/json")]
    [Route("api/v2/checkout")]
    [Authorize]
    public class CheckoutV2ApiController(
        ICustomerService customerService,
        IWorkContext workContext,
        IStoreContext storeContext,
        IShoppingCartService shoppingCartService,
        IOrderProcessingService orderProcessingService,
        ICompanyService companyService,
        ICompanyAllowancePaymentMethod companyAllowancePaymentMethod,
        IPaymentPluginManager paymentPluginManager,
        IPaymentService paymentService,
        ICheckoutAttributeService checkoutAttributeService,
        ICheckoutAttributeParser checkoutAttributeParser,
        IGenericAttributeService genericAttributeService,
        IAmeriaVPosPaymentService ameriaVPosPaymentService,
        ILocalizationService localizationService,
        IPriceFormatter priceFormatter,
        IDateTimeHelper dateTimeHelper,
        ILogger logger)
        : BaseApiController
    {
        // The exact format GetSummary formats AvailableScheduleDates with and PlaceOrder
        // parses ScheduleDate with - an unambiguous local (no offset/Z) round trip.
        private const string ScheduleDateFormat = "yyyy-MM-ddTHH:mm:ss";

        public class AddressV2Model
        {
            public int Id { get; set; }
            public string Label { get; set; }
        }

        public class CheckoutSummaryV2Model
        {
            public IList<AddressV2Model> Addresses { get; set; } = new List<AddressV2Model>();
            public int? SelectedAddressId { get; set; }
            // Each already in ScheduleDateFormat - pick one, echo it back verbatim to
            // PlaceOrder's ScheduleDate.
            public IList<string> AvailableScheduleDates { get; set; } = new List<string>();
            // Null when nothing to warn about. Otherwise { exceedsAllowance, requiresPayment,
            // amountDueValue, amountDueFormatted, message } - see BuildCheckoutWarningAsync.
            public object CheckoutWarning { get; set; }
        }

        public class PlaceOrderV2Model
        {
            public string ScheduleDate { get; set; }
            public string Notes { get; set; }

            // When set and the order needs a card payment for the allowance shortfall,
            // charges this saved card (api/v2/payment-cards) directly instead of
            // redirecting to the hosted pay page. See
            // IAmeriaVPosPaymentService.InitiateOrCompletePaymentAsync.
            public int? PaymentCardId { get; set; }
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            return Ok(await BuildSummaryAsync(customer));
        }

        // Mirrors CustomerApiController's real set-deliveryaddress/{addressId} (same
        // Billing+Shipping assignment), with one addition: v1's endpoint trusts any
        // addressId blindly with no ownership check - this verifies the address is
        // actually in the calling customer's own address book first.
        [HttpPost("address/{addressId}")]
        public async Task<IActionResult> SelectAddress(int addressId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();

            var ownAddresses = await customerService.GetAddressesByCustomerIdAsync(customer.Id);
            if (ownAddresses.All(a => a.Id != addressId))
                return Ok(new { success = false, message = "That address doesn't belong to your account." });

            customer.BillingAddressId = addressId;
            customer.ShippingAddressId = addressId;
            await customerService.UpdateCustomerAsync(customer);

            return Ok(await BuildSummaryAsync(customer));
        }

        [HttpPost("place-order")]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderV2Model model)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            if (!DateTime.TryParseExact(model?.ScheduleDate, ScheduleDateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var scheduleDateLocal))
            {
                return Ok(new
                {
                    success = false,
                    code = (int)OrderResultCode.ScheduleNotAllowed,
                    message = "Invalid delivery time - please refresh and try again."
                });
            }

            var company = await companyService.GetCompanyByCustomerIdAsync(customer.Id);
            var companyTimezone = TZConvert.GetTimeZoneInfo(company.TimeZone);
            var scheduleDateUtc = dateTimeHelper.ConvertToUtcTime(scheduleDateLocal, companyTimezone);

            if (!await IsScheduleDateAllowedAsync(companyTimezone, scheduleDateUtc))
            {
                await logger.ErrorAsync($"Order schedule was not allowed: {model.ScheduleDate}", customer: customer);
                return Ok(new
                {
                    success = false,
                    code = (int)OrderResultCode.ScheduleNotAllowed,
                    message = "We're sorry, but your scheduled delivery time has passed. Please refresh and pick a new time."
                });
            }

            if (!await orderProcessingService.IsMinimumOrderPlacementIntervalValidAsync(customer.Id, store.Id))
            {
                return Ok(new
                {
                    success = false,
                    code = (int)OrderResultCode.None,
                    message = await localizationService.GetResourceAsync("Checkout.MinOrderPlacementInterval")
                });
            }

            if (!string.IsNullOrEmpty(model.Notes))
                await SaveNotesAttributeAsync(customer, store, model.Notes);

            var processPaymentRequest = new ProcessPaymentRequest();
            paymentService.GenerateOrderGuid(processPaymentRequest);
            processPaymentRequest.StoreId = store.Id;
            processPaymentRequest.CustomerId = customer.Id;
            processPaymentRequest.ScheduleDate = scheduleDateUtc;
            processPaymentRequest.OrderSource = OrderSource.Mobile;

            var isAmeriaVPosActive = await paymentPluginManager.IsPluginActiveAsync("Payments.AmeriaVPos", customer, store.Id);
            processPaymentRequest.PaymentMethodSystemName = isAmeriaVPosActive
                ? "Payments.AmeriaVPos"
                : "Payments.CheckMoneyOrder";

            var placeOrderResult = await orderProcessingService.PlaceOrderAsync(processPaymentRequest);

            if (!placeOrderResult.Success)
            {
                var errors = placeOrderResult.Errors ?? new List<string>();
                var isAddressError = errors.Any(error => !string.IsNullOrEmpty(error)
                    && (error.Contains("Billing address", StringComparison.OrdinalIgnoreCase)
                        || error.Contains("Shipping address", StringComparison.OrdinalIgnoreCase)));

                return Ok(new
                {
                    success = false,
                    code = (int)(isAddressError ? OrderResultCode.InvalidDeliveryAddress : OrderResultCode.None),
                    message = string.Join(", ", errors)
                });
            }

            var placedOrder = placeOrderResult.PlacedOrder;

            if (!isAmeriaVPosActive)
            {
                return Ok(new
                {
                    success = true,
                    message = await localizationService.GetResourceAsync("Order.Placed.Successfully"),
                    orderId = placedOrder.Id
                });
            }

            var paymentResult = await ameriaVPosPaymentService.InitiateOrCompletePaymentAsync(placedOrder, "Mobile", model.PaymentCardId);

            if (!paymentResult.RequiresPayment)
            {
                return Ok(new
                {
                    success = true,
                    message = await localizationService.GetResourceAsync("Order.Placed.Successfully"),
                    orderId = placedOrder.Id
                });
            }

            var message = string.IsNullOrEmpty(paymentResult.PaymentUrl)
                ? "Your order was saved, but the card payment couldn't be started. Please try paying again from your Orders."
                : null;

            return Ok(new
            {
                success = false,
                requiresPayment = true,
                orderId = placedOrder.Id,
                paymentUrl = paymentResult.PaymentUrl,
                amountDueValue = paymentResult.AmountDue,
                amountDueFormatted = await priceFormatter.FormatPriceAsync(paymentResult.AmountDue),
                amountCoveredByAllowanceValue = paymentResult.AmountCoveredByAllowance,
                message
            });
        }

        private async Task<CheckoutSummaryV2Model> BuildSummaryAsync(Nop.Core.Domain.Customers.Customer customer)
        {
            var store = await storeContext.GetCurrentStoreAsync();
            var addresses = await customerService.GetAddressesByCustomerIdAsync(customer.Id);
            var availableScheduleDates = await orderProcessingService.GetAvailableDeliverTimesAsync();

            var model = new CheckoutSummaryV2Model
            {
                Addresses = addresses.Select(MapAddress).ToList(),
                SelectedAddressId = customer.ShippingAddressId,
                AvailableScheduleDates = availableScheduleDates
                    .Select(d => d.ToString(ScheduleDateFormat, CultureInfo.InvariantCulture))
                    .ToList()
            };

            if (availableScheduleDates.Count > 0)
            {
                var cartTotal = await GetCartTotalAsync(customer, store);
                model.CheckoutWarning = await BuildCheckoutWarningAsync(
                    customer, store.Id, availableScheduleDates.First(), cartTotal);
            }

            return model;
        }

        private static AddressV2Model MapAddress(Address address) => new()
        {
            Id = address.Id,
            Label = string.Join(", ", new[] { address.Address1, address.Address2, address.City }
                .Where(part => !string.IsNullOrWhiteSpace(part)))
        };

        private async Task<decimal> GetCartTotalAsync(Nop.Core.Domain.Customers.Customer customer, Nop.Core.Domain.Stores.Store store)
        {
            var cart = await shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

            decimal total = 0;
            foreach (var item in cart)
            {
                var (unitPrice, _, _) = await shoppingCartService.GetUnitPriceAsync(item, includeDiscounts: true);
                total += unitPrice * item.Quantity;
            }

            return total;
        }

        // Mirrors OrderApiController's private BuildCheckoutWarningAsync (v1's real,
        // load-bearing checkout warning) - same allowance-vs-total decision, same
        // self-pay-aware wording, just taking an already-local-and-valid scheduleDate
        // instead of a raw client string (see class header).
        private async Task<object> BuildCheckoutWarningAsync(
            Nop.Core.Domain.Customers.Customer customer, int storeId, DateTime scheduleDateLocal, decimal cartTotal)
        {
            try
            {
                var company = await companyService.GetCompanyByCustomerIdAsync(customer.Id);
                if (company == null)
                    return null;

                var companyTimezone = TZConvert.GetTimeZoneInfo(company.TimeZone);
                var orderDateUtc = dateTimeHelper.ConvertToUtcTime(scheduleDateLocal, companyTimezone);

                var balance = await companyAllowancePaymentMethod.GetCustomerRemainingAllowance(
                    new CustomerBalanceRequest { Customer = customer, OrderDateUtc = orderDateUtc });

                if (balance == null || cartTotal <= balance.RemainingAllowance)
                    return null;

                var selfPayAvailable = await paymentPluginManager.IsPluginActiveAsync(
                    "Payments.AmeriaVPos", customer, storeId);

                var message = await localizationService.GetResourceAsync(selfPayAvailable
                    ? "Mobile.Checkout.OverAllowance.WithSelfPay"
                    : "Mobile.Checkout.OverAllowance.NoSelfPay");

                var amountDue = selfPayAvailable ? cartTotal : decimal.Zero;

                return new
                {
                    exceedsAllowance = true,
                    requiresPayment = selfPayAvailable,
                    amountDueValue = amountDue,
                    amountDueFormatted = await priceFormatter.FormatPriceAsync(amountDue),
                    message
                };
            }
            catch
            {
                // A warning must never fail checkout - degrade to "no warning".
                return null;
            }
        }

        private async Task<bool> IsScheduleDateAllowedAsync(TimeZoneInfo companyTimezone, DateTime scheduleDateUtc)
        {
            var availableLocal = await orderProcessingService.GetAvailableDeliverTimesAsync();
            if (availableLocal.Count == 0)
                return false;

            var firstAvailableUtc = dateTimeHelper.ConvertToUtcTime(availableLocal.First(), companyTimezone);
            return scheduleDateUtc >= firstAvailableUtc;
        }

        private async Task SaveNotesAttributeAsync(Nop.Core.Domain.Customers.Customer customer, Nop.Core.Domain.Stores.Store store, string notes)
        {
            var allCheckoutAttributes = await checkoutAttributeService.GetAllCheckoutAttributesAsync(store.Id);
            var notesAttribute = allCheckoutAttributes.SingleOrDefault(a =>
                string.Equals(a.Name, "Notes", StringComparison.OrdinalIgnoreCase));

            if (notesAttribute == null)
            {
                await logger.ErrorAsync(
                    "Notes checkout attribute was provided but wasn't found in store configuration.",
                    customer: customer);
                return;
            }

            var checkoutAttributesXml = await genericAttributeService.GetAttributeAsync<string>(
                customer, NopCustomerDefaults.CheckoutAttributes, store.Id);

            checkoutAttributesXml = checkoutAttributeParser.RemoveCheckoutAttribute(checkoutAttributesXml, notesAttribute);
            checkoutAttributesXml = checkoutAttributeParser.AddCheckoutAttribute(checkoutAttributesXml, notesAttribute, notes);

            await genericAttributeService.SaveAttributeAsync(
                customer, NopCustomerDefaults.CheckoutAttributes, checkoutAttributesXml, store.Id);
        }
    }
}
