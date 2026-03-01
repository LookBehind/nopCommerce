using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Services.Companies;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Logging;
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

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ISettingService _settingService;
        private readonly IWorkContext _workContext;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICompanyService _companyService;
        private readonly ILogger _logger;
        private readonly IRepository<Order> _orderRepository;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public DeliveryTimeService(
            ISettingService settingService,
            IWorkContext workContext,
            IDateTimeHelper dateTimeHelper,
            ICompanyService companyService,
            ILogger logger,
            IRepository<Order> orderRepository,
            IStoreContext storeContext)
        {
            _settingService = settingService;
            _workContext = workContext;
            _dateTimeHelper = dateTimeHelper;
            _companyService = companyService;
            _logger = logger;
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
            
            // Use company's OrderAheadDays if available, otherwise use default
            return company?.OrderAheadDays ?? ORDER_AHEAD_DAYS_DEFAULT;
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
            var orderSettings = await _settingService.LoadSettingAsync<OrderSettings>(store.Id);

            if (string.IsNullOrWhiteSpace(orderSettings.ScheduleDate))
                return new List<DeliverySlot>();

            var raw = orderSettings.ScheduleDate.Trim();

            try
            {
                // New JSON format
                if (raw.StartsWith("["))
                {
                    var slots = JsonSerializer.Deserialize<List<DeliverySlot>>(raw, _jsonOptions);
                    return slots?
                        .Where(s => s.IsEnabled)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.DeliveryTime)
                        .ToList() ?? new List<DeliverySlot>();
                }

                // Legacy CSV format: "HH:MM:SS-HH:MM:SS-HH:MM:SS,..."
                return ParseLegacyCsv(raw);
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Error parsing delivery schedule configuration: {raw}", ex);
                return new List<DeliverySlot>();
            }
        }

        private List<DeliverySlot> ParseLegacyCsv(string csv)
        {
            var slots = new List<DeliverySlot>();
            var scheduleDateValues = csv.Split(',');
            var sortOrder = 0;

            foreach (var scheduleDate in scheduleDateValues)
            {
                var parts = scheduleDate.Split('-');
                if (parts.Length < 3)
                    continue;

                try
                {
                    var openTime = TimeSpan.Parse(parts[0]);
                    var cutoffTime = TimeSpan.Parse(parts[1]);
                    var deliveryTime = TimeSpan.Parse(parts[2]);

                    slots.Add(new DeliverySlot
                    {
                        OpenTime = openTime,
                        CutoffTime = cutoffTime,
                        DeliveryTime = deliveryTime,
                        IsEnabled = true,
                        SortOrder = sortOrder++
                    });
                }
                catch
                {
                    // Skip malformed entries
                }
            }

            return slots.OrderBy(s => s.DeliveryTime).ToList();
        }

        /// <summary>
        /// Gets the count of non-cancelled orders for each delivery time
        /// </summary>
        public virtual async Task<Dictionary<DateTime, int>> GetOrderCountsByDeliveryTimesAsync(List<DateTime> deliveryTimes)
        {
            if (deliveryTimes == null || deliveryTimes.Count == 0)
                return new Dictionary<DateTime, int>();

            // Get unique dates to query
            var dates = deliveryTimes.Select(dt => dt.Date).Distinct().ToList();

            // Query all non-cancelled, non-deleted orders for those dates
            var orders = await _orderRepository.Table
                .Where(o => !o.Deleted &&
                            (OrderStatus)o.OrderStatusId != OrderStatus.Cancelled &&
                            dates.Contains(o.ScheduleDate.Date))
                .Select(o => o.ScheduleDate)
                .ToListAsync();

            // Count orders per delivery time (matching both date and time of day)
            var result = new Dictionary<DateTime, int>();
            foreach (var deliveryTime in deliveryTimes)
            {
                var count = orders.Count(o => o.Date == deliveryTime.Date &&
                                              o.TimeOfDay.Hours == deliveryTime.TimeOfDay.Hours &&
                                              o.TimeOfDay.Minutes == deliveryTime.TimeOfDay.Minutes);
                result[deliveryTime] = count;
            }

            return result;
        }

        #endregion
    }
}
