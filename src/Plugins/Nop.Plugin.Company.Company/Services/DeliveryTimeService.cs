using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Services.Companies;
using Nop.Services.Helpers;
using Nop.Services.Orders;
using TimeZoneConverter;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Delivery time service implementation
    /// </summary>
    public partial class DeliveryTimeService : IDeliveryTimeService
    {
        #region Fields

        private const int ORDER_AHEAD_DAYS_DEFAULT = 14;

        private readonly IWorkContext _workContext;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICompanyService _companyService;
        private readonly IDeliverySlotService _deliverySlotService;
        private readonly IRepository<Order> _orderRepository;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public DeliveryTimeService(
            IWorkContext workContext,
            IDateTimeHelper dateTimeHelper,
            ICompanyService companyService,
            IDeliverySlotService deliverySlotService,
            IRepository<Order> orderRepository,
            IStoreContext storeContext)
        {
            _workContext = workContext;
            _dateTimeHelper = dateTimeHelper;
            _companyService = companyService;
            _deliverySlotService = deliverySlotService;
            _orderRepository = orderRepository;
            _storeContext = storeContext;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets available delivery times for the specified number of days ahead
        /// </summary>
        /// <param name="daysAhead">Number of days to look ahead (if null, uses company's OrderAheadDays setting)</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains a list of available delivery times
        /// </returns>
        public virtual async Task<List<DateTime>> GetAvailableDeliveryTimesAsync(int? daysAhead = null)
        {
            var deliveryTimes = new List<DateTime>();
            var slots = await GetDeliverySlotsAsync();
            
            if (slots.Count == 0)
                return deliveryTimes;

            // Use company's OrderAheadDays if not specified
            var actualDaysAhead = daysAhead ?? await GetMaxDaysAheadAsync();

            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
            var timezoneInfo = company == null
                ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                : TZConvert.GetTimeZoneInfo(company.TimeZone);

            var now = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, 
                TimeZoneInfo.Utc, 
                timezoneInfo);

            // Generate delivery times for each day up to actualDaysAhead
            for (var day = 0; day <= actualDaysAhead; day++)
            {
                var targetDate = now.Date.AddDays(day);
                var dailySlots = await GetDeliveryTimesForDateAsync(targetDate);
                deliveryTimes.AddRange(dailySlots);
            }

            return deliveryTimes.OrderBy(dt => dt).ToList();
        }

        /// <summary>
        /// Gets delivery times for a specific date
        /// </summary>
        /// <param name="date">The target date</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains a list of available delivery times for the date
        /// </returns>
        public virtual async Task<List<DateTime>> GetDeliveryTimesForDateAsync(DateTime date)
        {
            var deliveryTimes = new List<DateTime>();
            var slots = await GetDeliverySlotsAsync();

            if (slots.Count == 0)
                return deliveryTimes;

            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
            var timezoneInfo = company == null
                ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                : TZConvert.GetTimeZoneInfo(company.TimeZone);

            var now = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, timezoneInfo);
            var isToday = date.Date == now.Date;

            foreach (var slot in slots)
            {
                var deliveryTime = date.Date.Add(slot.DeliveryTime);
                
                if (isToday)
                {
                    // For today, check if we're still before the cutoff time
                    var lastOrderTime = date.Date.Add(slot.CutoffTime);
                    if (now <= lastOrderTime)
                    {
                        deliveryTimes.Add(deliveryTime);
                    }
                }
                else if (date.Date > now.Date)
                {
                    // For future dates, all slots are available
                    deliveryTimes.Add(deliveryTime);
                }
            }

            return deliveryTimes.OrderBy(dt => dt).ToList();
        }

        /// <summary>
        /// Validates if a delivery time is still available for ordering
        /// </summary>
        /// <param name="deliveryTime">The delivery time to validate</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains true if available, false otherwise
        /// </returns>
        public virtual async Task<bool> IsDeliveryTimeAvailableAsync(DateTime deliveryTime)
        {
            var maxDaysAhead = await GetMaxDaysAheadAsync();
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
            var timezoneInfo = company == null
                ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                : TZConvert.GetTimeZoneInfo(company.TimeZone);

            var now = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, 
                TimeZoneInfo.Utc, 
                timezoneInfo);

            // Check if delivery time is within allowed future range
            if (deliveryTime.Date > now.Date.AddDays(maxDaysAhead))
                return false;

            // Check if delivery time is in the past
            if (deliveryTime < now)
                return false;

            // Get available times for the delivery date and check if it's included
            var availableTimes = await GetDeliveryTimesForDateAsync(deliveryTime.Date);
            return availableTimes.Any(dt => Math.Abs((dt - deliveryTime).TotalMinutes) < 1);
        }

        /// <summary>
        /// Gets the maximum days ahead that customers can order
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the number of days ahead allowed for ordering
        /// </returns>
        public virtual async Task<int> GetMaxDaysAheadAsync()
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);

            // OrderAheadDays is a non-nullable int, so "company?.OrderAheadDays ?? DEFAULT"
            // only ever fell back to DEFAULT when company itself was null - a company row
            // whose OrderAheadDays is 0 (the type's default, and the only value reachable
            // today since the admin UI to set it is unreachable - see _CreateOrUpdate.Info.cshtml)
            // silently meant "0 days ahead" instead of "not configured". Treat <= 0 as unset.
            return company != null && company.OrderAheadDays > 0
                ? company.OrderAheadDays
                : ORDER_AHEAD_DAYS_DEFAULT;
        }

        /// <summary>
        /// Gets delivery time slots configuration
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains delivery slots configuration
        /// </returns>
        public virtual async Task<List<DeliverySlot>> GetDeliverySlotsAsync()
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            return (await _deliverySlotService.GetDeliverySlotsAsync(store.Id)).ToList();
        }

        /// <summary>
        /// Gets the count of non-cancelled orders for each delivery time
        /// </summary>
        public virtual async Task<Dictionary<DateTime, int>> GetOrderCountsByDeliveryTimesAsync(List<DateTime> deliveryTimes)
        {
            if (deliveryTimes == null || deliveryTimes.Count == 0)
                return new Dictionary<DateTime, int>();

            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            if (currentCustomer == null)
                return deliveryTimes.ToDictionary(dt => dt, _ => 0);

            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
            var timezoneInfo = company == null
                ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                : TZConvert.GetTimeZoneInfo(company.TimeZone);

            // The incoming deliveryTimes are wall-clock times in the display (company/customer)
            // time zone, but Order.ScheduleDate is stored in UTC. Comparing them directly is the
            // bug that made every count come back 0. Bracket the query by a generous UTC range
            // around the requested local dates, then convert each order's ScheduleDate from UTC
            // to the display zone before matching on date + time of day.
            var lowerBoundUtc = deliveryTimes.Min(dt => dt.Date).AddDays(-1);
            var upperBoundUtc = deliveryTimes.Max(dt => dt.Date).AddDays(1);

            var scheduleDatesUtc = await _orderRepository.Table
                .Where(o => o.CustomerId == currentCustomer.Id &&
                            !o.Deleted &&
                            (OrderStatus)o.OrderStatusId != OrderStatus.Cancelled &&
                            o.ScheduleDate >= lowerBoundUtc &&
                            o.ScheduleDate <= upperBoundUtc)
                .Select(o => o.ScheduleDate)
                .ToListAsync();

            // Convert UTC -> display time zone so it lines up with deliveryTimes
            var ordersLocal = scheduleDatesUtc
                .Select(utc => _dateTimeHelper.ConvertToUserTime(utc, TimeZoneInfo.Utc, timezoneInfo))
                .ToList();

            // Count orders per delivery time (matching both date and time of day)
            var result = new Dictionary<DateTime, int>();
            foreach (var deliveryTime in deliveryTimes)
            {
                var count = ordersLocal.Count(o => o.Date == deliveryTime.Date &&
                                                   o.TimeOfDay.Hours == deliveryTime.TimeOfDay.Hours &&
                                                   o.TimeOfDay.Minutes == deliveryTime.TimeOfDay.Minutes);
                result[deliveryTime] = count;
            }

            return result;
        }

        #endregion
    }
}
