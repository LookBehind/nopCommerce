using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Controllers;

namespace Nop.Plugin.Company.Company.Controllers
{
    /// <summary>
    /// Delivery time controller
    /// </summary>
    public class DeliveryTimeController : BasePublicController
    {
        #region Fields

        private readonly IDeliveryTimeService _deliveryTimeService;
        private readonly IWorkContext _workContext;
        private readonly ILocalizationService _localizationService;
        private readonly ICustomerService _customerService;

        #endregion

        #region Ctor

        public DeliveryTimeController(
            IDeliveryTimeService deliveryTimeService,
            IWorkContext workContext,
            ILocalizationService localizationService,
            ICustomerService customerService)
        {
            _deliveryTimeService = deliveryTimeService;
            _workContext = workContext;
            _localizationService = localizationService;
            _customerService = customerService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Set delivery time via AJAX
        /// </summary>
        /// <param name="deliveryTime">Selected delivery time</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public async Task<IActionResult> SetDeliveryTime(DateTime deliveryTime)
        {
            try
            {
                // Validate the delivery time
                if (!await _deliveryTimeService.IsDeliveryTimeAvailableAsync(deliveryTime))
                {
                    return Json(new { 
                        success = false, 
                        message = await _localizationService.GetResourceAsync("DeliveryTime.InvalidTime") 
                    });
                }

                // Save to session
                HttpContext.Session.Set("SelectedDeliveryTime", BitConverter.GetBytes(deliveryTime.Ticks));

                // Also save to cookie as backup (expires in 1 day)
                var cookieOptions = new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddDays(1),
                    HttpOnly = false,
                    SameSite = SameSiteMode.Lax
                };
                Response.Cookies.Append("SelectedDeliveryTime", deliveryTime.ToString("O"), cookieOptions);

                return Json(new { 
                    success = true, 
                    message = await _localizationService.GetResourceAsync("DeliveryTime.SelectionSaved") 
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = await _localizationService.GetResourceAsync("DeliveryTime.ErrorSaving") 
                });
            }
        }

        /// <summary>
        /// Get current delivery time
        /// </summary>
        /// <returns>JSON result with current delivery time</returns>
        [HttpGet]
        public async Task<IActionResult> GetDeliveryTime()
        {
            try
            {
                DateTime? selectedTime = null;

                // Try session first
                if (HttpContext.Session.TryGetValue("SelectedDeliveryTime", out var sessionBytes))
                {
                    var ticks = BitConverter.ToInt64(sessionBytes, 0);
                    selectedTime = new DateTime(ticks);
                }
                // Try cookie as fallback
                else if (HttpContext.Request.Cookies.TryGetValue("SelectedDeliveryTime", out var cookieValue) &&
                         DateTime.TryParse(cookieValue, out var cookieDateTime))
                {
                    selectedTime = cookieDateTime;
                }

                // Validate that the time is still available
                if (selectedTime.HasValue && !await _deliveryTimeService.IsDeliveryTimeAvailableAsync(selectedTime.Value))
                {
                    // Clear invalid selection
                    HttpContext.Session.Remove("SelectedDeliveryTime");
                    Response.Cookies.Delete("SelectedDeliveryTime");
                    selectedTime = null;
                }

                return Json(new { 
                    success = true, 
                    deliveryTime = selectedTime?.ToString("O"),
                    hasSelection = selectedTime.HasValue
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = await _localizationService.GetResourceAsync("DeliveryTime.ErrorRetrieving") 
                });
            }
        }

        /// <summary>
        /// Clear delivery time selection
        /// </summary>
        /// <returns>JSON result</returns>
        [HttpPost]
        public async Task<IActionResult> ClearDeliveryTime()
        {
            try
            {
                HttpContext.Session.Remove("SelectedDeliveryTime");
                Response.Cookies.Delete("SelectedDeliveryTime");

                return Json(new { 
                    success = true, 
                    message = await _localizationService.GetResourceAsync("DeliveryTime.SelectionCleared") 
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = await _localizationService.GetResourceAsync("DeliveryTime.ErrorClearing") 
                });
            }
        }

        #endregion
    }
}
