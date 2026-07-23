using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Pickup;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Web.Factories;
using Nop.Web.Models.Checkout;
using TimeZoneConverter;

namespace Nop.Plugin.Company.Company.Factories;

/// <summary>
/// Adds an AmeriaVPos self-pay warning to the storefront OPC confirm-order step,
/// mirroring the mobile app's Confirm-order sheet banner ("Order exceeds allowance,
/// you will be redirected for payment"). The core CheckoutModelFactory has no notion
/// of company allowance, so this appends the check rather than duplicating the whole
/// method.
/// </summary>
public class CheckoutModelFactory_Overriden : CheckoutModelFactory
{
    private readonly IWorkContext _workContext;
    private readonly IStoreContext _storeContext;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly ICompanyAllowancePaymentMethod _companyAllowancePaymentMethod;
    private readonly IDeliveryTimeStorageService _deliveryTimeStorageService;
    private readonly ICompanyService _companyService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ILocalizationService _localizationService;
    private readonly IPaymentPluginManager _paymentPluginManager;

    public CheckoutModelFactory_Overriden(
        IWorkContext workContext,
        IStoreContext storeContext,
        IOrderTotalCalculationService orderTotalCalculationService,
        ICompanyAllowancePaymentMethod companyAllowancePaymentMethod,
        IDeliveryTimeStorageService deliveryTimeStorageService,
        ICompanyService companyService,
        IDateTimeHelper dateTimeHelper,
        ILocalizationService localizationService,
        IPaymentPluginManager paymentPluginManager,
        AddressSettings addressSettings,
        CommonSettings commonSettings,
        IAddressModelFactory addressModelFactory,
        IAddressService addressService,
        ICountryService countryService,
        ICurrencyService currencyService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        IOrderProcessingService orderProcessingService,
        IPaymentService paymentService,
        IPickupPluginManager pickupPluginManager,
        IPriceFormatter priceFormatter,
        IRewardPointService rewardPointService,
        IShippingPluginManager shippingPluginManager,
        IShippingService shippingService,
        IShoppingCartService shoppingCartService,
        IStateProvinceService stateProvinceService,
        IStoreMappingService storeMappingService,
        ITaxService taxService,
        OrderSettings orderSettings,
        PaymentSettings paymentSettings,
        RewardPointsSettings rewardPointsSettings,
        ShippingSettings shippingSettings)
        : base(addressSettings,
            commonSettings,
            addressModelFactory,
            addressService,
            countryService,
            currencyService,
            customerService,
            genericAttributeService,
            localizationService,
            orderProcessingService,
            orderTotalCalculationService,
            paymentPluginManager,
            paymentService,
            pickupPluginManager,
            priceFormatter,
            rewardPointService,
            shippingPluginManager,
            shippingService,
            shoppingCartService,
            stateProvinceService,
            storeContext,
            storeMappingService,
            taxService,
            workContext,
            orderSettings,
            paymentSettings,
            rewardPointsSettings,
            shippingSettings,
            dateTimeHelper,
            companyService)
    {
        _workContext = workContext;
        _storeContext = storeContext;
        _orderTotalCalculationService = orderTotalCalculationService;
        _companyAllowancePaymentMethod = companyAllowancePaymentMethod;
        _deliveryTimeStorageService = deliveryTimeStorageService;
        _companyService = companyService;
        _dateTimeHelper = dateTimeHelper;
        _localizationService = localizationService;
        _paymentPluginManager = paymentPluginManager;
    }

    public override async Task<CheckoutConfirmModel> PrepareConfirmOrderModelAsync(IList<ShoppingCartItem> cart)
    {
        var model = await base.PrepareConfirmOrderModelAsync(cart);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        // Only relevant if AmeriaVPos is actually the active/selectable payment method -
        // a customer on a different payment method never gets redirected for card payment.
        var isAmeriaVPosActive = await _paymentPluginManager.IsPluginActiveAsync("Payments.AmeriaVPos", customer, store.Id);
        if (!isAmeriaVPosActive)
            return model;

        var cartTotals = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);
        if (cartTotals.shoppingCartTotal is not decimal total || total <= 0)
            return model;

        // Use the customer's actual selected delivery date (converted to the company's
        // timezone/UTC), same as CheckoutController_Overriden.OpcConfirmOrder - not
        // DateTime.UtcNow, which the payment-method-step description uses only as a
        // preview approximation before a delivery time is even picked.
        var deliveryTime = await _deliveryTimeStorageService.GetSelectedDeliveryTimeAsync(customer, store.Id);
        DateTime orderDateUtc;
        if (deliveryTime.HasValue)
        {
            var company = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
            var timeZoneInfo = company == null
                ? await _dateTimeHelper.GetCustomerTimeZoneAsync(customer)
                : TZConvert.GetTimeZoneInfo(company.TimeZone);
            orderDateUtc = _dateTimeHelper.ConvertToUtcTime(deliveryTime.Value, timeZoneInfo);
        }
        else
        {
            orderDateUtc = DateTime.UtcNow;
        }

        var balance = await _companyAllowancePaymentMethod.GetCustomerRemainingAllowance(
            new CustomerBalanceRequest { Customer = customer, OrderDateUtc = orderDateUtc });

        // Never split a single order between allowance and card - see the comment on
        // IAmeriaVPosPaymentService.InitiateOrCompletePaymentAsync for why.
        var remainingAllowance = balance?.RemainingAllowance ?? 0M;
        if (remainingAllowance >= total)
            return model;

        model.AllowanceExceededWarning =
            await _localizationService.GetResourceAsync("Plugins.Payments.AmeriaVPos.Description.FullSelfPay");

        return model;
    }
}
