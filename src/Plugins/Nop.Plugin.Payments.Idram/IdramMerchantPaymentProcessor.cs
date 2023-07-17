// Decompiled with JetBrains decompiler
// Type: Nop.Plugin.Payments.Idram.IdramMerchantPaymentProcessor
// Assembly: Nop.Plugin.Payments.Idram, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: ACB0BCDA-94BA-4FEB-8098-86C5829BC6E3
// Assembly location: C:\Workspace\MySnacks\Idram\Nop.Plugin.Payments.Idram.dll

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Web.Framework;

namespace Nop.Plugin.Payments.Idram
{
    public class IdramMerchantPaymentProcessor : BasePlugin, IPaymentMethod, IPlugin
    {
        // keep in sync with CheckMoneyOrderPaymentProcessor.CompanyBenefitExemptionRole
        private const string CompanyBenefitExemptionRole = "Allowance Excempt";
        
        private readonly IWebHelper _webHelper;
        private readonly IPaymentService _paymentService;
        private readonly IdramMerchantSettings _idramMerchantSettings;
        private readonly ISettingService _settingService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IRepository<Order> _orderRepository;
        private readonly IStoreContext _storeContext;
        private readonly ICompanyService _companyService;
        private readonly IGenericAttributeService _genericAttribute;
        private readonly ICustomerService _customerService;

        private static readonly HashSet<string> _languages = new(new[] {"EN", "RU", "AM"});

        public IdramMerchantPaymentProcessor(
            IWebHelper webHelper,
            IPaymentService paymentService,
            IdramMerchantSettings idramMerchantSettings,
            ISettingService settingService,
            ILocalizationService localizationService,
            IWorkContext workContext,
            IOrderTotalCalculationService orderTotalCalculationService,
            IRepository<Order> orderRepository,
            IStoreContext storeContext,
            ICompanyService companyService, 
            IGenericAttributeService genericAttribute, 
            ICustomerService customerService)
        {
            _webHelper = webHelper;
            _paymentService = paymentService;
            _idramMerchantSettings = idramMerchantSettings;
            _settingService = settingService;
            _localizationService = localizationService;
            _workContext = workContext;
            _orderTotalCalculationService = orderTotalCalculationService;
            _orderRepository = orderRepository;
            _storeContext = storeContext;
            _companyService = companyService;
            _genericAttribute = genericAttribute;
            _customerService = customerService;
        }

        public bool SupportCapture => false;

        public bool SupportPartiallyRefund => false;

        public bool SupportRefund => false;

        public bool SupportVoid => false;

        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        public bool SkipPaymentInfo => true;

        public async Task PostProcessPaymentAsync(
            PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var order = postProcessPaymentRequest.Order;
            
            var customerCompanyLimit = await GetCustomerCompanyLimit();
            var scheduleDateCustomerOrdersTotal =
                await GetOrderDayTotal(order.ScheduleDate);

            var remainingAllowanceForScheduleDate = scheduleDateCustomerOrdersTotal >= customerCompanyLimit
                ? 0
                : customerCompanyLimit - scheduleDateCustomerOrdersTotal; 
            
            // Within the company's allowance
            if (remainingAllowanceForScheduleDate >= order.OrderTotal)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.OrderStatus = OrderStatus.Processing;

                await _orderRepository.UpdateAsync(postProcessPaymentRequest.Order);

                return;
            }

            var differenceToPay = order.OrderTotal - remainingAllowanceForScheduleDate;

            var form = new RemotePost {FormName = "IDramPaymentForm", Url = _idramMerchantSettings.PaymentUrl};

            var currentLanguage = await _workContext.GetWorkingLanguageAsync();
            form.Add("EDP_LANGUAGE",
                _languages.TryGetValue(currentLanguage?.UniqueSeoCode?.ToUpper(), out var language)
                    ? language
                    : "EN");

            form.Add("EDP_REC_ACCOUNT", _idramMerchantSettings.IdramId);
            form.Add("EDP_DESCRIPTION", $"Payment for order #{postProcessPaymentRequest.Order.Id}");

            var amountAsString = Convert.ToString(differenceToPay, new CultureInfo("en-US"));
            form.Add("EDP_AMOUNT", amountAsString);
            form.Add("EDP_BILL_NO", Convert.ToString(postProcessPaymentRequest?.Order?.Id));
            form.Add("EDP_URL_SUCCESS",
                $"{_webHelper.GetStoreLocation()}IdramMerchant/Success/{postProcessPaymentRequest?.Order?.Id}");
            form.Add("EDP_URL_FAIL", _webHelper.GetStoreLocation() + "IdramMerchant/Fail");
            form.Add("EDP_URL_CONFIRM", _webHelper.GetStoreLocation() + "IdramMerchant/Result");
            form.Add("EDP_REC_NAME", _idramMerchantSettings.MerchantEmail);
            form.Post();
        }

        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(
            CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult
            {
                Errors = new string[1] {"Recurring payment not supported"}
            });
        }

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));
            return (DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5.0
                ? Task.FromResult(false)
                : Task.FromResult(true);
        }

        public Task<CapturePaymentResult> CaptureAsync(
            CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult {Errors = new string[1] {"Capture method not supported"}});
        }

        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            var additionalFeeAsync = await _paymentService.CalculateAdditionalFeeAsync(cart,
                _idramMerchantSettings.AdditionalFee,
                _idramMerchantSettings.AdditionalFeePercentage);

            return additionalFeeAsync;
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(
            IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        public Task<string> GetPaymentMethodDescriptionAsync() => Task.FromResult(string.Empty);

        public override string GetConfigurationPageUrl() =>
            _webHelper.GetStoreLocation() + "Admin/IdramMerchant/Configure";

        public string GetPublicViewComponentName() => string.Empty;

        public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            var shoppingCartTotal = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);

            var todayCustomerLimit = await GetCustomerCompanyLimit();
            if (shoppingCartTotal.shoppingCartTotal > todayCustomerLimit)
                return false;

            var orderDayDate = await _genericAttribute.GetAttributeAsync(await _workContext.GetCurrentCustomerAsync(),
                OrderProcessingService.DeliveryTimeAttributeName,
                (await _storeContext.GetCurrentStoreAsync()).Id,
                DateTime.UtcNow.Date);
            
            var totalOrderTotal = await GetOrderDayTotal(orderDayDate);
            
            return todayCustomerLimit >= totalOrderTotal + shoppingCartTotal.shoppingCartTotal;
        }

        public Task<ProcessPaymentResult> ProcessPaymentAsync(
            ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult());
        }

        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(
            ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(
                new ProcessPaymentResult {Errors = new string[1] {"Recurring payment not supported"}});
        }

        public Task<RefundPaymentResult> RefundAsync(
            RefundPaymentRequest refundPaymentRequest)
        {
            return Task.FromResult(new RefundPaymentResult {Errors = new string[1] {"Refund method not supported"}});
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form) =>
            Task.FromResult((IList<string>)new List<string>());

        public Task<VoidPaymentResult> VoidAsync(
            VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult {Errors = new string[1] {"Void method not supported"}});
        }

        public override async Task InstallAsync()
        {
            await _settingService.SaveSettingAsync(new IdramMerchantSettings
            {
                UseSandbox = true, PaymentUrl = "https://banking.idram.am/Payment/GetPayment"
            });
            var localizationService = _localizationService;
            var dictionary = new Dictionary<string, string>();
            dictionary["Plugins.Payments.Idram.PageTitle.Fail"] = "Payment Fail";
            dictionary["Plugins.Payments.Idram.Checkout.Fail"] = "Failed Payment Process";
            dictionary["Plugins.Payments.Idram.Checkout.YourPaymentHasBeenFailed"] =
                "Payment Unsuccessful. Please contact administrator or try again";
            dictionary["Plugins.Payments.Idram.Checkout.Error.Continue"] = "Continue Checkout";
            dictionary["Plugins.Payments.Idram.Fields.PaymentUrl"] = "Payment Url";
            dictionary["Plugins.Payments.Idram.Fields.PaymentUrl.Hint"] =
                "Payment Url where we will pass the form data";
            dictionary["Plugins.Payments.Idram.Fields.IdramId"] = "Idram Id";
            dictionary["Plugins.Payments.Idram.Fields.IdramId.Hint"] =
                "IdramID of the merchant, which receives customer’s payment";
            dictionary["Plugins.Payments.Idram.Fields.SecretKey"] = "Secret Key";
            dictionary["Plugins.Payments.Idram.Fields.SecretKey.Hint"] = "Secret Key which has provided by Idram";
            dictionary["Plugins.Payments.Idram.Fields.MerchantEmail"] = "Merchant Email";
            dictionary["Plugins.Payments.Idram.Fields.MerchantEmail.Hint"] =
                "Email address, to which payment confirmation will be sent if “OK” reply was not \r\nreceived from merchant during payment confirmation process. If set, it overloads EMAIL \r\nfield value for the current operation";
            dictionary["Plugins.Payments.Idram.Fields.AdditionalFee"] = "Additional fee";
            dictionary["Plugins.Payments.Idram.Fields.AdditionalFee.Hint"] =
                "Enter additional fee to charge your customers.";
            dictionary["Plugins.Payments.Idram.Fields.AdditionalFeePercentage"] = "Additional fee. Use percentage";
            dictionary["Plugins.Payments.Idram.Fields.AdditionalFeePercentage.Hint"] =
                "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.";
            dictionary["Plugins.Payments.Idram.Fields.UseSandbox"] = "Use Sandbox";
            dictionary["Plugins.Payments.Idram.Fields.UseSandbox.Hint"] =
                "Check to enable Sandbox (testing environment).";

            await localizationService.AddLocaleResourceAsync(dictionary);
            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            await _settingService.DeleteSettingAsync<IdramMerchantSettings>();
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Idram");
            await base.UninstallAsync();
        }

        // keep in sync with CheckMoneyOrderPaymentProcessor.GetOrderDayTotal
        private async Task<decimal> GetOrderDayTotal(DateTime scheduleDateUtc)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var customer = await _workContext.GetCurrentCustomerAsync();

            var thatDayOrders = await _orderRepository.Table
                .Where(o => o.StoreId == store.Id && 
                            o.CustomerId == customer.Id && 
                            o.ScheduleDate.Date == scheduleDateUtc.Date &&
                            (OrderStatus)o.OrderStatusId != OrderStatus.Cancelled &&
                            (PaymentStatus)o.PaymentStatusId == PaymentStatus.Paid &&
                            !o.Deleted)
                .SumAsync(x => x.OrderTotal);

            return thatDayOrders;
        }

        // keep in sync with CheckMoneyOrderPaymentProcessor.GetCustomerCompanyLimit
        private async Task<decimal> GetCustomerCompanyLimit()
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var customerRoles = await _customerService.GetCustomerRolesAsync(currentCustomer);

            if (customerRoles.Any(role =>
                    string.Equals(role.Name, CompanyBenefitExemptionRole, StringComparison.OrdinalIgnoreCase)))
                return 0M;
            
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);

            return company?.AmountLimit ?? 0M;
        }
    }
}