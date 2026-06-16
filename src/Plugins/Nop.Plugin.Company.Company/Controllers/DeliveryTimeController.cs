using System;
using System.Linq;
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
        IDeliveryTimeStorageService deliveryTimeStorageService,
        IWorkContext workContext,
        ILocalizationService localizationService,
        ICustomerService customerService,
        IStoreContext storeContext,
        ILogger logger)
        : BasePublicController
    {
        
        /// <summary>
        /// Set delivery time via AJAX
        /// </summary>
        /// <param name="setDeliveryTimeRequest">Selected delivery time</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public async Task<IActionResult> SetDeliveryTime([FromBody]SetDeliveryTimeRequest setDeliveryTimeRequest)
        {
            var currentCustomer = await workContext.GetCurrentCustomerAsync();
            var currentStore = await storeContext.GetCurrentStoreAsync();
            try
            {
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

                await deliveryTimeStorageService.SaveSelectedDeliveryTimeAsync(currentCustomer,
                    setDeliveryTimeRequest.DeliveryTime, currentStore.Id);

                await logger.InformationAsync($"Updated delivery time of customer '{currentCustomer.Email}' to '{setDeliveryTimeRequest}'", 
                    customer: currentCustomer);
                
                return Json(new { 
                    success = true, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.SelectionSaved") 
                });
            }
            catch (Exception ex)
            {
                await logger.ErrorAsync($"Error saving delivery time for customer '{currentCustomer.Email}'", 
                    ex, 
                    customer: currentCustomer);
                
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
            var currentCustomer = await workContext.GetCurrentCustomerAsync();
            var currentStore = await storeContext.GetCurrentStoreAsync();

            try
            {
                var selectedTime = await deliveryTimeStorageService.GetSelectedDeliveryTimeAsync(
                    currentCustomer, currentStore.Id);

                bool isValid = true;
                bool shouldPrompt = false;
                string promptType = "none";
                string stateClass = "";

                // Validate that the time is still available
                if (selectedTime.HasValue)
                {
                    if (!await deliveryTimeService.IsDeliveryTimeAvailableAsync(selectedTime.Value))
                    {
                        await deliveryTimeStorageService.ClearSelectedDeliveryTimeAsync(currentCustomer, currentStore.Id);
                        selectedTime = null;
                        isValid = false;
                        shouldPrompt = true;
                        promptType = "selection-invalid";
                        stateClass = "selection-invalid";
                    }
                    else
                    {
                        stateClass = "has-selection";
                    }
                }
                else
                {
                    shouldPrompt = true;
                    promptType = "no-selection";
                    stateClass = "no-selection";
                }

                var possibleTimes = await deliveryTimeService.GetAvailableDeliveryTimesAsync();
                var orderCounts = await deliveryTimeService.GetOrderCountsByDeliveryTimesAsync(possibleTimes);
                var orderedDates = await deliveryTimeService.GetCurrentCustomerOrderDatesAsync();

                return Json(new {
                    success = true,
                    selectedDeliveryTime = selectedTime,
                    possibleDeliveryTimes = possibleTimes,
                    orderedDates = orderedDates,
                    orderCountsByTime = orderCounts.ToDictionary(
                        kvp => kvp.Key.ToString("yyyy-MM-ddTHH:mm:ss"),
                        kvp => kvp.Value),
                    isValid = isValid,
                    shouldPrompt = shouldPrompt,
                    promptType = promptType,
                    stateClass = stateClass,
                    message = selectedTime == null ? await localizationService.GetResourceAsync("DeliveryTime.Retrieved")
                        : string.Empty
                });
            }
            catch (Exception ex)
            {
                await logger.ErrorAsync("Error retrieving delivery time", ex, customer: currentCustomer);

                return Json(new {
                    success = false,
                    selectedDeliveryTime = (string)null,
                    possibleDeliveryTimes = await deliveryTimeService.GetAvailableDeliveryTimesAsync(),
                    isValid = false,
                    shouldPrompt = true,
                    promptType = "no-selection",
                    stateClass = "no-selection",
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
            var currentCustomer = await workContext.GetCurrentCustomerAsync();
            var currentStore = await storeContext.GetCurrentStoreAsync();
            
            try
            {
                await deliveryTimeStorageService.ClearSelectedDeliveryTimeAsync(currentCustomer, currentStore.Id);

                return Json(new { 
                    success = true, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.SelectionCleared") 
                });
            }
            catch (Exception ex)
            {
                await logger.ErrorAsync("Error clearing delivery time", ex, customer: currentCustomer);
                
                return Json(new { 
                    success = false, 
                    message = await localizationService.GetResourceAsync("DeliveryTime.ErrorClearing") 
                });
            }
        }
    }
}
