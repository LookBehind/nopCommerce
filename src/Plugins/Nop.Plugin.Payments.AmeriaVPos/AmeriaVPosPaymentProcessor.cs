using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Cms;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.AmeriaVPos.ScheduledTasks;
using Nop.Services.Catalog;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;
using IScheduleTaskService = Nop.Services.Tasks.IScheduleTaskService;

namespace Nop.Plugin.Payments.AmeriaVPos
{
    /// <summary>
    /// AmeriaBank vPOS payment processor. Real decision logic (allowance-vs-total,
    /// InitPayment) lives in the shared IAmeriaVPosPaymentService, not here directly,
    /// because the mobile order-confirmation API needs the exact same logic but a JSON
    /// response instead of an HTTP redirect - see IAmeriaVPosPaymentService.
    /// </summary>
    public class AmeriaVPosPaymentProcessor : BasePlugin, IPaymentMethod, IWidgetPlugin
    {
        #region Fields

        private readonly IWebHelper _webHelper;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAmeriaVPosPaymentService _ameriaVPosPaymentService;
        private readonly ISettingService _settingService;
        private readonly ILocalizationService _localizationService;
        private readonly IScheduleTaskService _scheduleTaskService;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ICompanyAllowancePaymentMethod _companyAllowancePaymentMethod;
        private readonly IPriceFormatter _priceFormatter;
        private readonly WidgetSettings _widgetSettings;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public AmeriaVPosPaymentProcessor(
            IWebHelper webHelper,
            IHttpContextAccessor httpContextAccessor,
            IAmeriaVPosPaymentService ameriaVPosPaymentService,
            ISettingService settingService,
            ILocalizationService localizationService,
            IScheduleTaskService scheduleTaskService,
            IWorkContext workContext,
            IStoreContext storeContext,
            IShoppingCartService shoppingCartService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ICompanyAllowancePaymentMethod companyAllowancePaymentMethod,
            IPriceFormatter priceFormatter,
            WidgetSettings widgetSettings,
            ILogger logger)
        {
            _webHelper = webHelper;
            _httpContextAccessor = httpContextAccessor;
            _ameriaVPosPaymentService = ameriaVPosPaymentService;
            _settingService = settingService;
            _localizationService = localizationService;
            _scheduleTaskService = scheduleTaskService;
            _workContext = workContext;
            _storeContext = storeContext;
            _shoppingCartService = shoppingCartService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _companyAllowancePaymentMethod = companyAllowancePaymentMethod;
            _priceFormatter = priceFormatter;
            _widgetSettings = widgetSettings;
            _logger = logger;
        }

        #endregion

        #region Properties

        //real refunds/voids go through a separate, explicitly-confirmed admin action
        //(AmeriaVPosController.RefundAmeriaPayment/CancelAmeriaPayment) - not these stock
        //IPaymentMethod hooks, which the standard order-detail admin buttons call directly
        public bool SupportCapture => false;
        public bool SupportPartiallyRefund => false;
        public bool SupportRefund => false;
        public bool SupportVoid => false;

        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        public bool SkipPaymentInfo => true;

        /// <summary>
        /// Hides this plugin's own "AmeriaVPos" entry on the admin Widgets list page -
        /// it's a payment plugin that happens to also render an admin order-detail block,
        /// not something a store owner would toggle from the widgets screen.
        /// </summary>
        public bool HideInWidgetList => true;

        #endregion

        #region Methods

        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            //no-op - the order needs to exist (with an Id) before we can create a payment
            //attempt row and call InitPayment, so the real logic runs in PostProcessPaymentAsync
            return Task.FromResult(new ProcessPaymentResult());
        }

        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var result = await _ameriaVPosPaymentService.InitiateOrCompletePaymentAsync(postProcessPaymentRequest.Order);

            //fully covered by allowance - InitiateOrCompletePaymentAsync already marked the
            //order Paid, nothing left to do; stock OPC flow continues on to CheckoutCompleted
            if (!result.RequiresPayment)
                return;

            if (string.IsNullOrEmpty(result.PaymentUrl))
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos: order {postProcessPaymentRequest.Order.Id} requires payment but InitPayment " +
                    "did not return a redirect URL - see prior error log entry for the InitPayment failure.");
                return;
            }

            _httpContextAccessor.HttpContext.Response.Redirect(result.PaymentUrl);
        }

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            return Task.FromResult((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds >= 5.0);
        }

        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
        }

        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await Task.FromResult(0M);
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        /// <summary>
        /// Rendered inline next to this method's radio button in the OPC payment-method
        /// list (see CheckoutModelFactory.PreparePaymentMethodModel). This is the split
        /// messaging the design calls for: no mention of a shortfall for a fully
        /// allowance-exempt customer, but an explicit "balance covers X, pay Y by card"
        /// note for a customer with a partial allowance who's over their limit for this
        /// cart. Uses today's date as the schedule-date approximation for this preview -
        /// the actual enforcement at order-placement time uses the real, validated
        /// order.ScheduleDate (see IAmeriaVPosPaymentService.InitiateOrCompletePaymentAsync).
        /// </summary>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
            if (!cart.Any())
                return string.Empty;

            var cartTotals = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);
            if (cartTotals.shoppingCartTotal is not decimal total || total <= 0)
                return string.Empty;

            var balance = await _companyAllowancePaymentMethod.GetCustomerRemainingAllowance(
                new CustomerBalanceRequest { Customer = customer, OrderDateUtc = DateTime.UtcNow.Date });

            var remainingAllowance = balance?.RemainingAllowance ?? 0M;
            var amountCoveredByAllowance = Math.Min(Math.Max(remainingAllowance, 0M), total);
            var amountDue = total - amountCoveredByAllowance;

            if (amountDue <= 0)
                return string.Empty;

            if (amountCoveredByAllowance <= 0)
                return await _localizationService.GetResourceAsync("Plugins.Payments.AmeriaVPos.Description.FullSelfPay");

            var covered = await _priceFormatter.FormatPriceAsync(amountCoveredByAllowance);
            var due = await _priceFormatter.FormatPriceAsync(amountDue);
            var format = await _localizationService.GetResourceAsync("Plugins.Payments.AmeriaVPos.Description.PartialSelfPay");
            return string.Format(format, covered, due);
        }

        public override string GetConfigurationPageUrl() =>
            $"{_webHelper.GetStoreLocation()}Admin/AmeriaVPos/Configure";

        public string GetPublicViewComponentName() => string.Empty;

        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //always visible - unlike Idram, this method handles the fully-covered case
            //internally (marks Paid, charges nothing), so there's no "would charge zero
            //unnecessarily" case to hide for
            return Task.FromResult(false);
        }

        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Use the AmeriaBank refund admin action instead" } });
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form) =>
            Task.FromResult((IList<string>)new List<string>());

        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Use the AmeriaBank cancel admin action instead" } });
        }

        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Renders the separate, explicitly-confirmed Refund/Cancel buttons on the admin
        /// Order Edit page via AmeriaVPosOrderActionsViewComponent - no core-view edits.
        /// </summary>
        public Task<IList<string>> GetWidgetZonesAsync()
        {
            return Task.FromResult<IList<string>>(new List<string> { AdminWidgetZones.OrderDetailsBlock });
        }

        public string GetWidgetViewComponentName(string widgetZone) => "AmeriaVPosOrderActions";

        public override async Task InstallAsync()
        {
            await _settingService.SaveSettingAsync(new AmeriaVPosSettings
            {
                UseSandbox = true,
                ApiBaseUrl = "https://servicestest.ameriabank.am/VPOS",
                PayBaseUrl = "https://servicestest.ameriabank.am/VPOS"
            });

            await _localizationService.AddLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.AmeriaVPos.Fields.ClientId"] = "Client ID",
                ["Plugins.Payments.AmeriaVPos.Fields.ClientId.Hint"] = "Merchant ID issued by AmeriaBank.",
                ["Plugins.Payments.AmeriaVPos.Fields.Username"] = "Username",
                ["Plugins.Payments.AmeriaVPos.Fields.Username.Hint"] = "Merchant username issued by AmeriaBank.",
                ["Plugins.Payments.AmeriaVPos.Fields.Password"] = "Password",
                ["Plugins.Payments.AmeriaVPos.Fields.Password.Hint"] = "Merchant password issued by AmeriaBank.",
                ["Plugins.Payments.AmeriaVPos.Fields.ApiBaseUrl"] = "API base URL",
                ["Plugins.Payments.AmeriaVPos.Fields.ApiBaseUrl.Hint"] = "Base URL for the vPOS REST API. AmeriaBank has not issued a production hostname yet - only change this away from the sandbox value once they have.",
                ["Plugins.Payments.AmeriaVPos.Fields.PayBaseUrl"] = "Pay page base URL",
                ["Plugins.Payments.AmeriaVPos.Fields.PayBaseUrl.Hint"] = "Base URL for the hosted pay page customers are redirected to.",
                ["Plugins.Payments.AmeriaVPos.Fields.UseSandbox"] = "Use sandbox",
                ["Plugins.Payments.AmeriaVPos.Fields.UseSandbox.Hint"] = "Check to enable the AmeriaBank sandbox (testing) environment.",
                ["Plugins.Payments.AmeriaVPos.PageTitle.Fail"] = "Payment Fail",
                ["Plugins.Payments.AmeriaVPos.Checkout.Fail"] = "Failed Payment Process",
                ["Plugins.Payments.AmeriaVPos.Checkout.YourPaymentHasBeenFailed"] =
                    "Payment unsuccessful. Please contact administrator or try again.",
                ["Plugins.Payments.AmeriaVPos.Checkout.Error.Continue"] = "Continue Checkout",
                ["Plugins.Payments.AmeriaVPos.Description.FullSelfPay"] = "You'll be redirected to complete your payment by card.",
                ["Plugins.Payments.AmeriaVPos.Description.PartialSelfPay"] = "Your balance covers {0} of this order - pay the remaining {1} by card to confirm.",
                ["Plugins.Payments.AmeriaVPos.OrderActions.Title"] = "AmeriaBank vPOS",
                ["Plugins.Payments.AmeriaVPos.OrderActions.ChargedAmount"] = "Amount charged via AmeriaBank: {0}",
                ["Plugins.Payments.AmeriaVPos.OrderActions.RefundAmount"] = "Amount to refund:",
                ["Plugins.Payments.AmeriaVPos.OrderActions.Refund"] = "Refund via AmeriaBank",
                ["Plugins.Payments.AmeriaVPos.OrderActions.Cancel"] = "Cancel via AmeriaBank"
            });

            if (!_widgetSettings.ActiveWidgetSystemNames.Contains("Payments.AmeriaVPos"))
            {
                _widgetSettings.ActiveWidgetSystemNames.Add("Payments.AmeriaVPos");
                await _settingService.SaveSettingAsync(_widgetSettings);
            }

            if (await _scheduleTaskService.GetTaskByTypeAsync(AmeriaVPosReconciliationTask.TASK_TYPE) == null)
            {
                await _scheduleTaskService.InsertTaskAsync(new Core.Domain.Tasks.ScheduleTask
                {
                    Enabled = true,
                    Name = AmeriaVPosReconciliationTask.TASK_NAME,
                    Type = AmeriaVPosReconciliationTask.TASK_TYPE,
                    Seconds = 300
                });
            }

            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            var reconciliationTask = await _scheduleTaskService.GetTaskByTypeAsync(AmeriaVPosReconciliationTask.TASK_TYPE);
            if (reconciliationTask != null)
                await _scheduleTaskService.DeleteTaskAsync(reconciliationTask);

            _widgetSettings.ActiveWidgetSystemNames.Remove("Payments.AmeriaVPos");
            await _settingService.SaveSettingAsync(_widgetSettings);

            await _settingService.DeleteSettingAsync<AmeriaVPosSettings>();
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.AmeriaVPos");
            await base.UninstallAsync();
        }

        #endregion
    }
}
