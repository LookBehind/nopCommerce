using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Storage;
using Nop.Core.Domain.Orders;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Stores;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Keeps the time-derived trigger jobs in sync with the configured delivery slots — one
    /// per-slot Hangfire recurring job firing 40 min before each slot (delivery-approaching) plus one
    /// per-store job after the last slot (day-closing). Mirrors <c>PreDeliveryNudgeReconciler</c>:
    /// per-slot exact-instant jobs, UTC crons derived from UTC+4 local slots, reconciled at boot and
    /// on change — never a poll. See docs/BACKGROUND-AGENTS.md §3.
    /// </summary>
    public class InsightsDeliveryTriggerReconciler
    {
        private const string ApproachPrefix = "insights-delivery-approaching-";
        private const string DayClosingPrefix = "insights-day-closing-";
        private const int LocalUtcOffsetHours = 4;
        private const int ApproachMinutes = 40;
        private const int DayClosingDelayMinutes = 30;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IInsightsEventService _events;
        private readonly ISettingService _settingService;
        private readonly IStoreService _storeService;
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly ILogger _logger;

        public InsightsDeliveryTriggerReconciler(
            IInsightsEventService events,
            ISettingService settingService,
            IStoreService storeService,
            IRecurringJobManager recurringJobManager,
            ILogger logger)
        {
            _events = events;
            _settingService = settingService;
            _storeService = storeService;
            _recurringJobManager = recurringJobManager;
            _logger = logger;
        }

        private class DeliverySlotDto
        {
            public string DeliveryTime { get; set; }
            public bool IsEnabled { get; set; } = true;
        }

        public async Task ReconcileAsync()
        {
            var desired = new HashSet<string>();
            try
            {
                if (_events.Enabled)
                {
                    foreach (var store in await _storeService.GetAllStoresAsync())
                    {
                        List<TimeSpan> slots;
                        try
                        {
                            var orderSettings = await _settingService.LoadSettingAsync<OrderSettings>(store.Id);
                            slots = ParseSlots(orderSettings.ScheduleDate);
                        }
                        catch (Exception e)
                        {
                            await _logger.WarningAsync($"Insights delivery reconciler: bad slots for store {store.Id}", e);
                            continue;
                        }
                        if (slots.Count == 0)
                            continue;

                        foreach (var slot in slots)
                        {
                            var hhmm = $"{slot.Hours:D2}:{slot.Minutes:D2}";
                            var jobId = $"{ApproachPrefix}{store.Id}-{slot.Hours:D2}{slot.Minutes:D2}";
                            desired.Add(jobId);
                            var storeId = store.Id;
                            _recurringJobManager.AddOrUpdate<IInsightsDeliveryTriggerJob>(
                                jobId, j => j.RunApproachingAsync(storeId, hhmm), BuildCronUtc(slot, -ApproachMinutes));
                        }

                        // One day-closing job per store, shortly after the last slot.
                        var last = slots.Max();
                        var dayJobId = $"{DayClosingPrefix}{store.Id}";
                        desired.Add(dayJobId);
                        var sid = store.Id;
                        _recurringJobManager.AddOrUpdate<IInsightsDeliveryTriggerJob>(
                            dayJobId, j => j.RunDayClosingAsync(sid), BuildCronUtc(last, DayClosingDelayMinutes));
                    }
                }

                RemoveStale(desired);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights delivery reconciler failed", ex);
            }
        }

        private static List<TimeSpan> ParseSlots(string scheduleDateRaw)
        {
            if (string.IsNullOrWhiteSpace(scheduleDateRaw) || !scheduleDateRaw.TrimStart().StartsWith("["))
                return new List<TimeSpan>();

            var dtos = JsonSerializer.Deserialize<List<DeliverySlotDto>>(scheduleDateRaw, JsonOptions) ?? new List<DeliverySlotDto>();
            var result = new List<TimeSpan>();
            foreach (var d in dtos)
                if (d.IsEnabled && TimeSpan.TryParse(d.DeliveryTime, out var ts))
                    result.Add(ts);
            return result.Distinct().OrderBy(t => t).ToList();
        }

        /// <summary>UTC cron for a UTC+4 local slot time shifted by <paramref name="deltaMinutes"/>.</summary>
        private static string BuildCronUtc(TimeSpan slotLocal, int deltaMinutes)
        {
            var local = slotLocal + TimeSpan.FromMinutes(deltaMinutes);
            var utc = local - TimeSpan.FromHours(LocalUtcOffsetHours);
            var minutes = ((int)utc.TotalMinutes % (24 * 60) + 24 * 60) % (24 * 60);
            return $"{minutes % 60} {minutes / 60} * * *";
        }

        private void RemoveStale(HashSet<string> desired)
        {
            try
            {
                var existing = JobStorage.Current.GetConnection().GetRecurringJobs()
                    .Select(j => j.Id)
                    .Where(id => id.StartsWith(ApproachPrefix, StringComparison.Ordinal)
                              || id.StartsWith(DayClosingPrefix, StringComparison.Ordinal));
                foreach (var stale in existing.Except(desired))
                    _recurringJobManager.RemoveIfExists(stale);
            }
            catch (Exception ex)
            {
                _logger.WarningAsync("Insights delivery reconciler: remove-stale failed", ex).GetAwaiter().GetResult();
            }
        }
    }
}
