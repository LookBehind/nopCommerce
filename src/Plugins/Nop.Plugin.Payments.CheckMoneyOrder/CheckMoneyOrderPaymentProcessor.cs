using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using System.Linq;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Payments;
using Nop.Data;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;

namespace Nop.Plugin.Payments.CheckMoneyOrder
{
    /// <summary>
    /// CheckMoneyOrder payment processor
    /// </summary>
    public class CheckMoneyOrderPaymentProcessor : BasePlugin, IPaymentMethod, ICompanyAllowancePaymentMethod
    {
        // keep in sync with IdramMerchantPaymentProcessor.CompanyBenefitExemptionRole
        private const string CompanyBenefitExemptionRole = "Allowance Excempt";
        
        #region Fields

        private readonly CheckMoneyOrderPaymentSettings _checkMoneyOrderPaymentSettings;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly IRepository<Order> _orderRepository;
        private readonly IStoreContext _storeContext;
        private readonly ICompanyService _companyService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IGenericAttributeService _genericAttribute;
        private readonly ICustomerService _customerService;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => false;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => false;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => false;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => false;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => true;

        #endregion
        
        #region Ctor

        public CheckMoneyOrderPaymentProcessor(CheckMoneyOrderPaymentSettings checkMoneyOrderPaymentSettings,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            ISettingService settingService,
            IShoppingCartService shoppingCartService,
            IWebHelper webHelper,
            IWorkContext workContext, 
            IRepository<Order> orderRepository, 
            IStoreContext storeContext, 
            ICompanyService companyService, 
            IOrderTotalCalculationService orderTotalCalculationService, 
            IGenericAttributeService genericAttribute, 
            ICustomerService customerService)
        {
            _checkMoneyOrderPaymentSettings = checkMoneyOrderPaymentSettings;
            _localizationService = localizationService;
            _paymentService = paymentService;
            _settingService = settingService;
            _shoppingCartService = shoppingCartService;
            _webHelper = webHelper;
            _workContext = workContext;
            _orderRepository = orderRepository;
            _storeContext = storeContext;
            _companyService = companyService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _genericAttribute = genericAttribute;
            _customerService = customerService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var customerCompanyLimit = await GetCustomerCompanyLimit();
            var scheduleDateCustomerOrdersTotal =
                await GetOrderDayTotal(processPaymentRequest.ScheduleDate);

            var remainingAllowanceForScheduleDate = scheduleDateCustomerOrdersTotal >= customerCompanyLimit
                ? 0
                : customerCompanyLimit - scheduleDateCustomerOrdersTotal; 
            
            var result = new ProcessPaymentResult();
            
            // Within the company's allowance
            if (remainingAllowanceForScheduleDate >= processPaymentRequest.OrderTotal)
            {
                result.NewPaymentStatus = PaymentStatus.Paid;
            }
            else
            {
                result.AddError($"Your remaining allowance ({remainingAllowanceForScheduleDate} AMD) is not enough to " +
                                $"purchase your current order ({processPaymentRequest.OrderTotal} AMD).");
            }
            
            return result;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //nothing
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the rue - hide; false - display.
        /// </returns>
        public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            if (_checkMoneyOrderPaymentSettings.ShippableProductRequired && !await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
                return true;

            var shoppingCartTotal = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);

            var todayCustomerLimit = await GetCustomerCompanyLimit();
            if (shoppingCartTotal.shoppingCartTotal > todayCustomerLimit)
                return true;

            var orderDayDate = await _genericAttribute.GetAttributeAsync(await _workContext.GetCurrentCustomerAsync(),
                OrderProcessingService.DeliveryTimeAttributeName,
                (await _storeContext.GetCurrentStoreAsync()).Id,
                DateTime.UtcNow.Date);
            
            var totalOrderTotal = await GetOrderDayTotal(orderDayDate);
            
            return todayCustomerLimit < totalOrderTotal + shoppingCartTotal.shoppingCartTotal;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the additional handling fee
        /// </returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await _paymentService.CalculateAdditionalFeeAsync(cart,
                _checkMoneyOrderPaymentSettings.AdditionalFee, _checkMoneyOrderPaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the capture payment result
        /// </returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //it's not a redirection payment method. So we always return false
            return Task.FromResult(false);
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of validating errors
        /// </returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the payment info holder
        /// </returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentCheckMoneyOrder/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "CheckMoneyOrder";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task InstallAsync()
        {
            //settings
            var settings = new CheckMoneyOrderPaymentSettings
            {
                DescriptionText = "<p>Mail Personal or Business Check, Cashier's Check or money order to:</p><p><br /><b>NOP SOLUTIONS</b> <br /><b>your address here,</b> <br /><b>New York, NY 10001 </b> <br /><b>USA</b></p><p>Notice that if you pay by Personal or Business Check, your order may be held for up to 10 days after we receive your check to allow enough time for the check to clear.  If you want us to ship faster upon receipt of your payment, then we recommend your send a money order or Cashier's check.</p><p>P.S. You can edit this text from admin panel.</p>"
            };
            await _settingService.SaveSettingAsync(settings);

            //locales
            await _localizationService.AddLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payment.CheckMoneyOrder.AdditionalFee"] = "Additional fee",
                ["Plugins.Payment.CheckMoneyOrder.AdditionalFee.Hint"] = "The additional fee.",
                ["Plugins.Payment.CheckMoneyOrder.AdditionalFeePercentage"] = "Additional fee. Use percentage",
                ["Plugins.Payment.CheckMoneyOrder.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payment.CheckMoneyOrder.DescriptionText"] = "Description",
                ["Plugins.Payment.CheckMoneyOrder.DescriptionText.Hint"] = "Enter info that will be shown to customers during checkout",
                ["Plugins.Payment.CheckMoneyOrder.PaymentMethodDescription"] = "Eligible for your company's provided benefits",
                ["Plugins.Payment.CheckMoneyOrder.ShippableProductRequired"] = "Shippable product required",
                ["Plugins.Payment.CheckMoneyOrder.ShippableProductRequired.Hint"] = "An option indicating whether shippable products are required in order to display this payment method during checkout."
            });

            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<CheckMoneyOrderPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payment.CheckMoneyOrder");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <remarks>
        /// return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
        /// for example, for a redirection payment method, description may be like this: "You will be redirected to PayPal site to complete the payment"
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payment.CheckMoneyOrder.PaymentMethodDescription");
        }

        #endregion

        // keep in sync with IdramMerchantPaymentProcessor.GetOrderDayTotal
        private async Task<decimal> GetOrderDayTotal(DateTime scheduleDateUtc, Customer customer = null)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            customer ??= await _workContext.GetCurrentCustomerAsync();

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

        // Keep in sync with IdramMerchantPaymentProcessor.GetCustomerCompanyLimit
        private async Task<decimal> GetCustomerCompanyLimit(Customer customer = null)
        {
            var currentCustomer = customer ?? await _workContext.GetCurrentCustomerAsync();
            var customerRoles = await _customerService.GetCustomerRolesAsync(currentCustomer);

            if (customerRoles.Any(role =>
                    string.Equals(role.Name, CompanyBenefitExemptionRole, StringComparison.OrdinalIgnoreCase)))
                return 0M;
            
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);

            return company?.AmountLimit ?? 0M;
        }

        public async Task<decimal> GetCustomerRemainingAllowance(DateTime date, Customer customer = null)
        {
            var customerCompanyLimit = await GetCustomerCompanyLimit(customer);
            if (customerCompanyLimit == 0)
                return 0;

            var orderDayTotal = await GetOrderDayTotal(date, customer);
            return orderDayTotal > customerCompanyLimit ? 0 : customerCompanyLimit - orderDayTotal;
        }
    }
}