using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Http.Extensions;
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
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Web.Controllers;
using Nop.Web.Factories;
using Nop.Web.Framework.Controllers;
using Nop.Web.Models.Checkout;

namespace Nop.Plugin.Company.Company.Controllers;

public class CheckoutController_Overriden: CheckoutController
{
    private readonly IDeliveryTimeStorageService _deliveryTimeStorageService;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly IDeliveryTimeService _deliveryTimeService;
    
    public CheckoutController_Overriden(
        IDeliveryTimeStorageService deliveryTimeStorageService,
        IDeliveryTimeService deliveryTimeService,
        
        AddressSettings addressSettings, 
        CustomerSettings customerSettings, 
        IAddressAttributeParser addressAttributeParser, 
        IAddressService addressService, 
        ICheckoutModelFactory checkoutModelFactory, 
        ICountryService countryService, 
        ICustomerService customerService, 
        IGenericAttributeService genericAttributeService, 
        ILocalizationService localizationService, 
        ILogger logger, 
        IOrderProcessingService orderProcessingService, 
        IOrderService orderService, 
        IPaymentPluginManager paymentPluginManager, 
        IPaymentService paymentService, 
        IProductService productService, 
        IShippingService shippingService, 
        IShoppingCartService shoppingCartService, 
        IStoreContext storeContext, 
        IWebHelper webHelper, 
        IWorkContext workContext, 
        OrderSettings orderSettings, 
        PaymentSettings paymentSettings, 
        RewardPointsSettings rewardPointsSettings, 
        ShippingSettings shippingSettings, 
        IDateTimeHelper dateTimeHelper, 
        ICompanyService companyService) 
        : base(addressSettings, 
            customerSettings, 
            addressAttributeParser, 
            addressService, 
            checkoutModelFactory, 
            countryService, 
            customerService, 
            genericAttributeService, 
            localizationService, 
            logger, 
            orderProcessingService, 
            orderService, 
            paymentPluginManager, 
            paymentService, 
            productService, 
            shippingService, 
            shoppingCartService, 
            storeContext, 
            webHelper, 
            workContext, 
            orderSettings, 
            paymentSettings, 
            rewardPointsSettings, 
            shippingSettings, 
            dateTimeHelper, 
            companyService)
    {
        _deliveryTimeStorageService = deliveryTimeStorageService;
        _storeContext = storeContext;
        _workContext = workContext;
        _deliveryTimeService = deliveryTimeService;
    }

    public override async Task<IActionResult> OpcSaveShipping(CheckoutShippingAddressModel model, 
        IFormCollection form)
    {
        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var deliveryTime = await _deliveryTimeStorageService.GetSelectedDeliveryTimeAsync(
            currentCustomer, 
            currentStore.Id);

        if (!deliveryTime.HasValue)
        {
            throw new Exception("Please select a delivery time from the header before proceeding with checkout.");
        }

        if (!await _deliveryTimeService.IsDeliveryTimeAvailableAsync(deliveryTime.Value))
        {
            throw new Exception("The selected delivery time is no longer available. Please select a new delivery time.");
        }
        
        return await base.OpcSaveShipping(model, form);
    }

    public override async Task<IActionResult> OpcConfirmOrder()
    {
        var currentCustomer = await _workContext.GetCurrentCustomerAsync();
        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var deliveryTime = await _deliveryTimeStorageService.GetSelectedDeliveryTimeAsync(
            currentCustomer,
            currentStore.Id);

        if (deliveryTime.HasValue)
        {
            var processPaymentRequest = HttpContext.Session.Get<ProcessPaymentRequest>("OrderPaymentInfo")
                                        ?? new ProcessPaymentRequest();
            processPaymentRequest.ScheduleDate = deliveryTime.Value;
            HttpContext.Session.Set("OrderPaymentInfo", processPaymentRequest);
        }

        return await base.OpcConfirmOrder();
    }
}