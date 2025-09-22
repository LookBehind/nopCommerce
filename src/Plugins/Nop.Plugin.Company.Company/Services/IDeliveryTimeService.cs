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
    }

    /// <summary>
    /// Delivery slot configuration
    /// </summary>
    public partial record DeliverySlot
    {
        /// <summary>
        /// Display label for the slot (e.g., "Lunch")
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Last time orders can be placed for this slot
        /// </summary>
        public TimeSpan LastOrderTime { get; set; }

        /// <summary>
        /// Delivery time for this slot
        /// </summary>
        public TimeSpan DeliveryTime { get; set; }
    }
}

