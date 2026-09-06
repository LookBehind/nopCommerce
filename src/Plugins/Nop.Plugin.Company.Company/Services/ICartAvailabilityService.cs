using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Cart availability service interface - checks the current cart against a candidate
    /// delivery date for vendor closures and unpublished products. Single source of truth
    /// shared by the live checkout availability check and the final OpcSaveShipping guard.
    /// </summary>
    public partial interface ICartAvailabilityService
    {
        /// <summary>
        /// Gets the cart items that would be unavailable for the given delivery date
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <param name="selectedDeliveryTimeLocal">Selected delivery time, as stored by the picker (company-local wall-clock time)</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of unavailable cart items (empty if all items are available)
        /// </returns>
        Task<IList<UnavailableCartItem>> GetUnavailableItemsAsync(Customer customer, int storeId, DateTime selectedDeliveryTimeLocal);
    }

    /// <summary>
    /// Reason a cart item is unavailable for the selected delivery date
    /// </summary>
    public enum CartItemUnavailableReason
    {
        /// <summary>
        /// The item's vendor is closed on the selected date (working-day pattern or a day-off override)
        /// </summary>
        VendorClosed,

        /// <summary>
        /// The product itself is unpublished
        /// </summary>
        ProductUnavailable
    }

    /// <summary>
    /// An unavailable cart item, with enough detail to display and to remove it
    /// </summary>
    public partial record UnavailableCartItem
    {
        /// <summary>
        /// Shopping cart item identifier (needed to remove the item)
        /// </summary>
        public int CartItemId { get; set; }

        /// <summary>
        /// Product identifier
        /// </summary>
        public int ProductId { get; set; }

        /// <summary>
        /// Product name
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// Vendor name (null when the reason isn't vendor-related)
        /// </summary>
        public string VendorName { get; set; }

        /// <summary>
        /// Why this item is unavailable
        /// </summary>
        public CartItemUnavailableReason Reason { get; set; }

        /// <summary>
        /// Human-readable message for this item
        /// </summary>
        public string Message { get; set; }
    }
}
