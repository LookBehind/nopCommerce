using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Idram.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Idram.Controllers
{
    public class IdramMerchantController : BasePaymentController
    {
        private readonly IPermissionService _permissionService;
        private readonly IStoreContext _storeContext;
        private readonly ISettingService _settingService;
        private readonly INotificationService _notificationService;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly ILogger _logger;

        public IdramMerchantController(
            IPermissionService permissionService,
            IStoreContext storeContext,
            ISettingService settingService,
            INotificationService notificationService,
            ILocalizationService localizationService,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            ILogger logger)
        {
            _permissionService = permissionService;
            _storeContext = storeContext;
            _settingService = settingService;
            _notificationService = notificationService;
            _localizationService = localizationService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _logger = logger;
        }

        [AuthorizeAdmin]
        [Area("Admin")]
        public async Task<IActionResult> Configure()
        {
            var flag1 = await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods);
            if (!flag1)
                return AccessDeniedView();
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var idramPaymentSettings = await _settingService.LoadSettingAsync<IdramMerchantSettings>(storeScope);
            var model = new ConfigurationModel
            {
                UseSandbox = idramPaymentSettings.UseSandbox,
                PaymentUrl = idramPaymentSettings.PaymentUrl,
                IdramId = idramPaymentSettings.IdramId,
                SecretKey = idramPaymentSettings.SecretKey,
                MerchantEmail = idramPaymentSettings.MerchantEmail,
                AdditionalFee = idramPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = idramPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope <= 0)
                return View("~/Plugins/Payments.Idram/Views/Configure.cshtml", model);

            var configurationModel1 = model;
            var flag2 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.UseSandbox, storeScope);
            configurationModel1.UseSandbox_OverrideForStore = flag2;
            configurationModel1 = null;
            var configurationModel2 = model;
            var flag3 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.PaymentUrl, storeScope);
            configurationModel2.PaymentUrl_OverrideForStore = flag3;
            configurationModel2 = null;
            var configurationModel3 = model;
            var flag4 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.IdramId, storeScope);
            configurationModel3.IdramId_OverrideForStore = flag4;
            configurationModel3 = null;
            var configurationModel4 = model;
            var flag5 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.SecretKey, storeScope);
            configurationModel4.SecretKey_OverrideForStore = flag5;
            configurationModel4 = null;
            var configurationModel5 = model;
            var flag6 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.MerchantEmail,
                storeScope);
            configurationModel5.MerchantEmail_OverrideForStore = flag6;
            configurationModel5 = null;
            var configurationModel6 = model;
            var flag7 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.AdditionalFee,
                storeScope);
            configurationModel6.AdditionalFee_OverrideForStore = flag7;
            configurationModel6 = null;
            var configurationModel7 = model;
            var flag8 = await _settingService.SettingExistsAsync(idramPaymentSettings, x => x.AdditionalFeePercentage,
                storeScope);
            configurationModel7.AdditionalFeePercentage_OverrideForStore = flag8;
            configurationModel7 = null;
            return View("~/Plugins/Payments.Idram/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area("Admin")]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            var flag = await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods);
            if (!flag)
                return AccessDeniedView();
            if (!ModelState.IsValid)
            {
                var actionResult = await Configure();
                return actionResult;
            }

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var idramPaymentSettings = await _settingService.LoadSettingAsync<IdramMerchantSettings>(storeScope);
            idramPaymentSettings.UseSandbox = model.UseSandbox;
            idramPaymentSettings.PaymentUrl = model.PaymentUrl;
            idramPaymentSettings.IdramId = model.IdramId;
            idramPaymentSettings.SecretKey = model.SecretKey;
            idramPaymentSettings.MerchantEmail = model.MerchantEmail;
            idramPaymentSettings.AdditionalFee = model.AdditionalFee;
            idramPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings, x => x.UseSandbox,
                (model.UseSandbox_OverrideForStore ? 1 : 0) != 0, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings, x => x.PaymentUrl,
                (model.PaymentUrl_OverrideForStore ? 1 : 0) != 0, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings, x => x.IdramId,
                (model.IdramId_OverrideForStore ? 1 : 0) != 0, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings, x => x.SecretKey,
                (model.SecretKey_OverrideForStore ? 1 : 0) != 0, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings, x => x.MerchantEmail,
                (model.MerchantEmail_OverrideForStore ? 1 : 0) != 0, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings, x => x.AdditionalFee,
                (model.AdditionalFee_OverrideForStore ? 1 : 0) != 0, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(idramPaymentSettings,
                x => x.AdditionalFeePercentage, (model.AdditionalFeePercentage_OverrideForStore ? 1 : 0) != 0,
                storeScope, false);
            await _settingService.ClearCacheAsync();
            var inotificationService = _notificationService;
            var str = await _localizationService.GetResourceAsync("Admin.Plugins.Saved");
            inotificationService.SuccessNotification(str);
            var actionResult1 = await Configure();
            return actionResult1;
        }

        public IActionResult Success(int id) => RedirectToRoute("CheckoutCompleted", new {orderId = id});

        public async Task<IActionResult> Fail()
        {
            try
            {
                int orderId;
                int.TryParse((string)HttpContext.Request.Query["EDP_BILL_NO"], out orderId);

                await _logger.InformationAsync(string.Format("Method {0} called successfully with response : {1}",
                    nameof(Fail), orderId));

                var order = await _orderService.GetOrderByIdAsync(orderId);
                if (order != null)
                {
                    await _orderProcessingService.ReOrderAsync(order);
                    await _orderProcessingService.DeleteOrderAsync(order);
                }
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Getting exception from Method : Fail", ex);
            }

            return View("~/Plugins/Payments.Idram/Views/PaymentFail.cshtml");
        }

        [HttpPost]
        public async Task<string> Result()
        {
            try
            {
                var preCheck = Convert.ToString((string)HttpContext.Request.Form["EDP_PRECHECK"]);
                var billNo = Convert.ToString((string)HttpContext.Request.Form["EDP_BILL_NO"]);
                var recAccount = Convert.ToString((string)HttpContext.Request.Form["EDP_REC_ACCOUNT"]);
                var amount = Convert.ToString((string)HttpContext.Request.Form["EDP_AMOUNT"]);

                var currentStoreId = (await _storeContext.GetCurrentStoreAsync())?.Id ?? 0;
                var idramPaymentSettings =
                    await _settingService.LoadSettingAsync<IdramMerchantSettings>(currentStoreId);

                await _logger.InformationAsync("Method Result is called with response : EDP_PRECHECK = " + preCheck +
                                               ", EDP_BILL_NO = " + billNo + ", EDP_REC_ACCOUNT = " + recAccount +
                                               ", EDP_AMOUNT = " + amount);

                // Check order validity
                var orderId = Convert.ToInt32(billNo);
                var order = await _orderService.GetOrderByIdAsync(orderId);
                if (order == null)
                {
                    await _logger.WarningAsync($"Order with id {orderId} is not found");
                    return "FAIL";
                }

                if (order.PaymentStatus != PaymentStatus.Pending)
                {
                    await _logger.WarningAsync($"Order with id {orderId} is not in pending status {order.PaymentStatus}");
                    return "FAIL";
                }
                
                var amountDecimal = Convert.ToDecimal(amount, new CultureInfo("en-US"));
                if (order.OrderTotal != amountDecimal)
                {
                    await _logger.WarningAsync($"Transaction amount is not equal to order total, EDP_AMOUNT = {amountDecimal}, " +
                                               $"order total = {order.OrderTotal}");
                    return "FAIL";
                }
                
                // Check if received merchant ID is ours
                if (!string.Equals(recAccount, idramPaymentSettings.IdramId, StringComparison.OrdinalIgnoreCase))
                {
                    await _logger.WarningAsync($"Received merchant id is not ours, EDP_REC_ACCOUNT = {recAccount}");
                    return "FAIL";
                }
                
                if (!string.IsNullOrWhiteSpace(preCheck) &&
                    string.Equals(preCheck, "YES", StringComparison.OrdinalIgnoreCase))
                {
                    return "OK";
                }

                var payerAccount = Convert.ToString((string)HttpContext.Request.Form["EDP_PAYER_ACCOUNT"]);
                var transactionId = Convert.ToString((string)HttpContext.Request.Form["EDP_TRANS_ID"]);
                var checkSum = Convert.ToString((string)HttpContext.Request.Form["EDP_CHECKSUM"]);
                var transactionDate = Convert.ToString((string)HttpContext.Request.Form["EDP_TRANS_DATE"]);

                await _logger.InformationAsync("Method Result is called with response : EDP_BILL_NO = " + billNo +
                                               ", EDP_REC_ACCOUNT = " + recAccount + ", EDP_AMOUNT = " + amount +
                                               ", EDP_PAYER_ACCOUNT = " + payerAccount + ", EDP_TRANS_ID = " +
                                               transactionId +
                                               ", EDP_CHECKSUM = " + checkSum + ", EDP_TRANS_DATE = " +
                                               transactionDate);

                if (!string.IsNullOrWhiteSpace(payerAccount) &&
                    !string.IsNullOrWhiteSpace(transactionId) &&
                    !string.IsNullOrWhiteSpace(checkSum))
                {
                    var response = recAccount + ":" + amount + ":" + idramPaymentSettings.SecretKey + ":" + billNo +
                                   ":" + payerAccount + ":" + transactionId + ":" + transactionDate;
                    
                    var hash = HashHelper.CreateHash(Encoding.UTF8.GetBytes(response), "MD5");
                    
                    if (string.Equals(checkSum, hash, StringComparison.Ordinal))
                    {
                        order.OrderStatus = OrderStatus.Processing;
                        order.AuthorizationTransactionId = transactionId;
                        order.CaptureTransactionId = transactionId;
                        await _orderProcessingService.MarkOrderAsPaidAsync(order);
                        await _orderService.UpdateOrderAsync(order);
                        
                        return "OK";
                    }
                    else
                    {
                        await _logger.WarningAsync($"Hash mismatch on payment, source str = {response}," +
                                                       $"calculated hash = {hash}, received from idram = {checkSum}");
                    }
                }
                else
                {
                    await _logger.InformationAsync("Missing required fields");
                }
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Getting exception from Method : Result", ex);
            }

            return "FAIL";
        }
    }
}