using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Web.Controllers;

namespace Nop.Plugin.Company.Company.Controllers
{
    public class SetDeliveryTimeRequest
    {
        public DateTime DeliveryTime { get; set; }
    }

    public class CheckCartAvailabilityRequest
    {
        public DateTime DeliveryTime { get; set; }
    }

    public class RemoveUnavailableCartItemsRequest
    {
        public List<int> CartItemIds { get; set; } = new();
    }

    /// <summary>
    /// Delivery time controller
    /// </summary>
    public class DeliveryTimeController(
        IDeliveryTimeService deliveryTimeService,
        IDeliveryTimeStorageService deliveryTimeStorageService,
        ICartAvailabilityService cartAvailabilityService,
        IShoppingCartService shoppingCartService,
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

                return Json(new {
                    success = true,
                    selectedDeliveryTime = selectedTime,
                    possibleDeliveryTimes = possibleTimes,
                    // Only slots that actually have orders are sent; the client defaults the rest to 0
                    orderCountsByTime = orderCounts
                        .Where(kvp => kvp.Value > 0)
                        .ToDictionary(
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

        /// <summary>
        /// Checks the current cart against a candidate delivery date for vendor closures and
        /// unpublished products, so the inline checkout picker can surface it live (the same
        /// check also guards final checkout submission in CheckoutController_Overriden)
        /// </summary>
        /// <param name="request">Candidate delivery time</param>
        /// <returns>JSON result listing any unavailable cart items</returns>
        [HttpPost]
        public async Task<IActionResult> CheckCartAvailability([FromBody] CheckCartAvailabilityRequest request)
        {
            var currentCustomer = await workContext.GetCurrentCustomerAsync();
            var currentStore = await storeContext.GetCurrentStoreAsync();

            var unavailableItems = await cartAvailabilityService.GetUnavailableItemsAsync(
                currentCustomer, currentStore.Id, request.DeliveryTime);

            return Json(new
            {
                available = !unavailableItems.Any(),
                unavailableItems = unavailableItems.Select(item => new
                {
                    cartItemId = item.CartItemId,
                    productId = item.ProductId,
                    productName = item.ProductName,
                    vendorName = item.VendorName,
                    reason = item.Reason.ToString(),
                    message = item.Message
                })
            });
        }

        /// <summary>
        /// Removes the given cart items (used by the inline checkout picker's
        /// "remove unavailable items and continue" action)
        /// </summary>
        /// <param name="request">Cart item identifiers to remove</param>
        /// <returns>JSON result</returns>
        [HttpPost]
        public async Task<IActionResult> RemoveUnavailableCartItems([FromBody] RemoveUnavailableCartItemsRequest request)
        {
            var currentCustomer = await workContext.GetCurrentCustomerAsync();
            var currentStore = await storeContext.GetCurrentStoreAsync();

            try
            {
                var cart = await shoppingCartService.GetShoppingCartAsync(
                    currentCustomer, ShoppingCartType.ShoppingCart, currentStore.Id);

                foreach (var item in cart)
                {
                    if (!request.CartItemIds.Contains(item.Id))
                        continue;

                    await shoppingCartService.UpdateShoppingCartItemAsync(currentCustomer, item.Id,
                        item.AttributesXml, item.CustomerEnteredPrice,
                        item.RentalStartDateUtc, item.RentalEndDateUtc, quantity: 0);
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await logger.ErrorAsync("Error removing unavailable cart items", ex, customer: currentCustomer);

                return Json(new { success = false });
            }
        }
    }
}
