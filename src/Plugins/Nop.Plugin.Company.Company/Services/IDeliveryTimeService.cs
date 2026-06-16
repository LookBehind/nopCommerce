using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Delivery time service interface
    /// </summary>
    public partial interface IDeliveryTimeService
    {
        /// <summary>
        /// Gets available delivery times for the specified number of days ahead
        /// </summary>
        /// <param name="daysAhead">Number of days to look ahead (if null, uses company's OrderAheadDays setting)</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains a list of available delivery times
        /// </returns>
        Task<List<DateTime>> GetAvailableDeliveryTimesAsync(int? daysAhead = null);

        /// <summary>
        /// Gets delivery times for a specific date
        /// </summary>
        /// <param name="date">The target date</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains a list of available delivery times for the date
        /// </returns>
        Task<List<DateTime>> GetDeliveryTimesForDateAsync(DateTime date);

        /// <summary>
        /// Validates if a delivery time is still available for ordering
        /// </summary>
        /// <param name="deliveryTime">The delivery time to validate</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains true if available, false otherwise
        /// </returns>
        Task<bool> IsDeliveryTimeAvailableAsync(DateTime deliveryTime);

        /// <summary>
        /// Gets the maximum days ahead that customers can order
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the number of days ahead allowed for ordering
        /// </returns>
        Task<int> GetMaxDaysAheadAsync();

        /// <summary>
        /// Gets delivery time slots configuration
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains delivery slots configuration
        /// </returns>
        Task<List<DeliverySlot>> GetDeliverySlotsAsync();

        /// <summary>
        /// Gets the count of the current customer's non-cancelled orders for each delivery time,
        /// so the picker can show how many orders the customer has pre-placed per slot (and flag
        /// days that already have orders).
        /// </summary>
        /// <param name="deliveryTimes">List of delivery times (in the display time zone) to count orders for</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains a dictionary mapping delivery time to order count
        /// </returns>
        Task<Dictionary<DateTime, int>> GetOrderCountsByDeliveryTimesAsync(List<DateTime> deliveryTimes);
    }

    /// <summary>
    /// Delivery slot configuration
    /// </summary>
    public partial record DeliverySlot
    {
        /// <summary>
        /// Time the ordering window opens
        /// </summary>
        public TimeSpan OpenTime { get; set; }

        /// <summary>
        /// Last time orders can be placed for this slot (cutoff)
        /// </summary>
        public TimeSpan CutoffTime { get; set; }

        /// <summary>
        /// When the delivery happens
        /// </summary>
        public TimeSpan DeliveryTime { get; set; }

        /// <summary>
        /// Whether this slot is currently active
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Display order (0-based)
        /// </summary>
        public int SortOrder { get; set; }
    }
}

