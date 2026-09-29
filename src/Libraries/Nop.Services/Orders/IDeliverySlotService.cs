using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nop.Services.Orders
{
    /// <summary>
    /// Parses the store's configured delivery slots (OrderSettings.ScheduleDate) and answers
    /// scheduling questions that depend on the current time - shared between the storefront
    /// catalog (which needs to know the earliest date a customer could actually order for,
    /// to avoid checking vendor availability against a date that's no longer orderable) and
    /// the Company plugin's delivery time picker (which owns the full picker UI).
    /// </summary>
    public partial interface IDeliverySlotService
    {
        /// <summary>
        /// Gets the store's configured delivery slots (enabled only, ordered for display)
        /// </summary>
        /// <param name="storeId">Store identifier</param>
        /// <returns>Delivery slots</returns>
        Task<IList<DeliverySlot>> GetDeliverySlotsAsync(int storeId);

        /// <summary>
        /// Gets the earliest calendar date a customer could still place an order for, given
        /// the current time in the supplied time zone: today if any slot's cutoff hasn't
        /// passed yet, otherwise tomorrow (future dates have no cutoff restriction). Returns
        /// today if no delivery slots are configured (nothing to restrict against).
        /// </summary>
        /// <param name="storeId">Store identifier</param>
        /// <param name="timeZone">Time zone to evaluate "now" and "today" in</param>
        /// <returns>The earliest orderable calendar date, in <paramref name="timeZone"/></returns>
        Task<DateTime> GetEarliestOrderableDateAsync(int storeId, TimeZoneInfo timeZone);
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
