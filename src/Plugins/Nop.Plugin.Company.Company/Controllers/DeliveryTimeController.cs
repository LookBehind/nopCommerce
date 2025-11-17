using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Web.Controllers;

namespace Nop.Plugin.Company.Company.Controllers
{
    public class SetDeliveryTimeRequest
    {
        public DateTime DeliveryTime { get; set; }
    }
    
    /// <summary>
    /// Delivery time controller
    /// </summary>
    public class DeliveryTimeController(
        IDeliveryTimeService deliveryTimeService,
        IWorkContext workContext,
        ILocalizationService localizationService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        IStoreContext storeContext,
        ILogger logger)
        : BasePublicController
    {
        private const string SELECTED_DELIVERY_TIME_KEY = nameof(SELECTED_DELIVERY_TIME_KEY);
        
        /// <summary>
        /// Set delivery time via AJAX
        /// </summary>
        /// <param name="setDeliveryTimeRequest">Selected delivery time</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public async Task<IActionResult> SetDeliveryTime([FromBody]SetDeliveryTimeRequest setDeliveryTimeRequest)
        {
            try
            {
                var currentCustomer = await workContext.GetCurrentCustomerAsync();
                var currentStore = await storeContext.GetCurrentStoreAsync();
                
                // Validate the delivery time
                if (!await deliveryTimeService.IsDeliveryTimeAvailableAsync(setDeliveryTimeRequest.DeliveryTime))
                {
                    await logger.WarningAsync($"Delivery time '{setDeliveryTimeRequest.DeliveryTime}' is not available", 
                        customer: currentCustomer);
                    return Json(new { 
                        success = false, 
                        message = await localizationService.GetResourceAsync("DeliveryTime.InvalidTime") 
                    });
                }

                await genericAttributeService.SaveAttributeAsync(currentCustomer,
                    SELECTED_DELIVERY_TIME_KEY, setDeliveryTimeRequest.DeliveryTime, currentStore.Id);

                await logger.InformationAsync($"Updated delivery time of customer '{currentCustomer.Email}' to '{setDeliveryTimeRequest}'", 
                    customer: currentCustomer);
                
                return Json(new { 
                    success = true, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.SelectionSaved") 
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.ErrorSaving") 
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
                var currentCustomer = await workContext.GetCurrentCustomerAsync();
                var currentStore = await storeContext.GetCurrentStoreAsync();
                
                var selectedTime = await genericAttributeService.GetAttributeAsync<DateTime?>(
                    currentCustomer, SELECTED_DELIVERY_TIME_KEY, currentStore.Id);

                // Validate that the time is still available
                if (selectedTime.HasValue && 
                    !await deliveryTimeService.IsDeliveryTimeAvailableAsync(selectedTime.Value))
                {
                    await genericAttributeService.SaveAttributeAsync<DateTime?>(currentCustomer,
                        SELECTED_DELIVERY_TIME_KEY, null, currentStore.Id);
                    selectedTime = null;
                }

                return Json(new { 
                    success = true,
                    selectedDeliveryTime = selectedTime,
                    possibleDeliveryTimes = await deliveryTimeService.GetAvailableDeliveryTimesAsync(),
                    message = selectedTime == null ? await localizationService.GetResourceAsync("DeliveryTime.Retrieved") 
                        : string.Empty
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    selectedDeliveryTime = (string)null,
                    possibleDeliveryTimes = await deliveryTimeService.GetAvailableDeliveryTimesAsync(),
                    message = await localizationService.GetResourceAsync("DeliveryTime.ErrorRetrieving") 
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
                var currentCustomer = await workContext.GetCurrentCustomerAsync();
                var currentStore = await storeContext.GetCurrentStoreAsync();
                await genericAttributeService.SaveAttributeAsync<DateTime?>(currentCustomer,
                    SELECTED_DELIVERY_TIME_KEY, null, currentStore.Id);

                return Json(new { 
                    success = true, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.SelectionCleared") 
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.ErrorClearing") 
                });
            }
        }
    }
}
