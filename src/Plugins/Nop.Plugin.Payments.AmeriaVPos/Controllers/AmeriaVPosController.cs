using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.AmeriaVPos.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.AmeriaVPos.Controllers
{
    public class AmeriaVPosController : BasePaymentController
    {
        #region Fields

        private readonly IPermissionService _permissionService;
        private readonly IStoreContext _storeContext;
        private readonly ISettingService _settingService;
        private readonly INotificationService _notificationService;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderService _orderService;
        private readonly IAmeriaVPosPaymentService _ameriaVPosPaymentService;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public AmeriaVPosController(
            IPermissionService permissionService,
            IStoreContext storeContext,
            ISettingService settingService,
            INotificationService notificationService,
            ILocalizationService localizationService,
            IOrderService orderService,
            IAmeriaVPosPaymentService ameriaVPosPaymentService,
            ILogger logger)
        {
            _permissionService = permissionService;
            _storeContext = storeContext;
            _settingService = settingService;
            _notificationService = notificationService;
            _localizationService = localizationService;
            _orderService = orderService;
            _ameriaVPosPaymentService = ameriaVPosPaymentService;
            _logger = logger;
        }

        #endregion

        #region Configuration

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<AmeriaVPosSettings>(storeScope);

            var model = new ConfigurationModel
            {
                UseSandbox = settings.UseSandbox,
                ClientId = settings.ClientId,
                Username = settings.Username,
                Password = settings.Password,
                ApiBaseUrl = settings.ApiBaseUrl,
                PayBaseUrl = settings.PayBaseUrl,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope <= 0)
                return View("~/Plugins/Payments.AmeriaVPos/Views/Configure.cshtml", model);

            model.UseSandbox_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.UseSandbox, storeScope);
            model.ClientId_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ClientId, storeScope);
            model.Username_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Username, storeScope);
            model.Password_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Password, storeScope);
            model.ApiBaseUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ApiBaseUrl, storeScope);
            model.PayBaseUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.PayBaseUrl, storeScope);

            return View("~/Plugins/Payments.AmeriaVPos/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<AmeriaVPosSettings>(storeScope);

            settings.UseSandbox = model.UseSandbox;
            settings.ClientId = model.ClientId;
            settings.Username = model.Username;
            settings.Password = model.Password;
            settings.ApiBaseUrl = model.ApiBaseUrl;
            settings.PayBaseUrl = model.PayBaseUrl;

            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ClientId, model.ClientId_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Username, model.Username_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Password, model.Password_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ApiBaseUrl, model.ApiBaseUrl_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.PayBaseUrl, model.PayBaseUrl_OverrideForStore, storeScope, false);

            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        #endregion

        #region Payment return / admin actions

        /// <summary>
        /// AmeriaBank redirects the customer's browser here after payment. We never trust
        /// the querystring - only the authoritative GetPaymentDetails pull inside
        /// ResolvePaymentAsync. A mobile-originated attempt (system browser, no OPC/cart
        /// session) gets bounced on into the app via the mysnacks:// deep link instead of
        /// nopCommerce's web CheckoutCompleted/PaymentFail pages, which have nothing to
        /// render against for a mobile order.
        ///
        /// The query param is named "msOrderId", NOT "orderId" - found live against the
        /// sandbox 2026-07-22: AmeriaBank's own redirect appends its own querystring
        /// (their OrderID, resposneCode, paymentID, opaque, description), and their OrderID
        /// field is also literally named "orderId". Using the same key silently overwrote
        /// ours with the AmeriaVPosPaymentAttempt row's own Id (their OrderID) instead of the
        /// real nopCommerce Order.Id, sending every real bank round-trip to the wrong "order".
        /// </summary>
        public async Task<IActionResult> BackUrlReturn(int msOrderId)
        {
            var order = await _orderService.GetOrderByIdAsync(msOrderId);
            if (order == null)
            {
                await _logger.WarningAsync($"AmeriaVPos BackUrlReturn: order {msOrderId} not found");
                return View("~/Plugins/Payments.AmeriaVPos/Views/PaymentFail.cshtml");
            }

            var result = await _ameriaVPosPaymentService.ResolvePaymentAsync(order);

            if (result.Platform == "Mobile")
            {
                return View("~/Plugins/Payments.AmeriaVPos/Views/MobileReturn.cshtml", order.Id);
            }

            if (result.Status == Domain.AmeriaVPosPaymentAttemptStatus.Paid.ToString())
                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

            return View("~/Plugins/Payments.AmeriaVPos/Views/PaymentFail.cshtml");
        }

        /// <summary>
        /// Deliberately separate from the stock order-detail Refund button (which staff
        /// use for benefit-funded orders with no real money involved) - this moves real
        /// card money, so it gets its own explicitly-confirmed action.
        /// </summary>
        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> RefundAmeriaPayment(int orderId, decimal amount)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageOrders))
                return AccessDeniedView();

            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
                return RedirectToAction("List", "Order", new { area = AreaNames.Admin });

            try
            {
                var success = await _ameriaVPosPaymentService.RefundAsync(order, amount);
                if (success)
                    _notificationService.SuccessNotification("AmeriaBank refund completed.");
                else
                    _notificationService.ErrorNotification("AmeriaBank refund failed - check the log for details.");
            }
            catch (Exception exc)
            {
                await _logger.ErrorAsync($"AmeriaVPos RefundAmeriaPayment failed for order {orderId}", exc);
                _notificationService.ErrorNotification(exc.Message);
            }

            return RedirectToAction("Edit", "Order", new { id = orderId, area = AreaNames.Admin });
        }

        /// <summary>
        /// Deliberately separate from the stock order-detail Cancel/Void button - see
        /// RefundAmeriaPayment.
        /// </summary>
        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> CancelAmeriaPayment(int orderId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageOrders))
                return AccessDeniedView();

            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
                return RedirectToAction("List", "Order", new { area = AreaNames.Admin });

            try
            {
                var success = await _ameriaVPosPaymentService.CancelAsync(order);
                if (success)
                    _notificationService.SuccessNotification("AmeriaBank payment cancelled.");
                else
                    _notificationService.ErrorNotification("AmeriaBank cancel failed - check the log for details.");
            }
            catch (Exception exc)
            {
                await _logger.ErrorAsync($"AmeriaVPos CancelAmeriaPayment failed for order {orderId}", exc);
                _notificationService.ErrorNotification(exc.Message);
            }

            return RedirectToAction("Edit", "Order", new { id = orderId, area = AreaNames.Admin });
        }

        #endregion
    }
}
