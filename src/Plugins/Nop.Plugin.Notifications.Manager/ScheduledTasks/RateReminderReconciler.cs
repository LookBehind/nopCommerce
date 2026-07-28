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

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks;

/// <summary>
/// Keeps one Hangfire recurring job per configured delivery slot (per store), each firing once a
/// day exactly 1 hour after that slot. Re-run whenever `OrderSettings.ScheduleDate` changes
/// (via <see cref="EventConsumer.RateReminderSettingConsumer"/>) and once at app boot - not on a
/// poll. Customer-facing mirror of <see cref="PreDeliveryNudgeReconciler"/> (vendor-facing,
/// -1h before the slot); this one isn't gated by NotificationManagerSettings.VendorDeliveryMiniAppEnabled
/// since it's unrelated to that feature. See docs/plans/2026-07-29-rate-reminder-slot-jobs.md.
/// </summary>
public class RateReminderReconciler
{
    private const string RECURRING_JOB_ID_PREFIX = "rate-reminder-";
    private const int LOCAL_UTC_OFFSET_HOURS = 4;

    private readonly ISettingService _settingService;
    private readonly IStoreService _storeService;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger _logger;

    public RateReminderReconciler(
        ISettingService settingService,
        IStoreService storeService,
        IRecurringJobManager recurringJobManager,
        ILogger logger)
    {
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ReconcileAsync()
    {
        var desiredJobIds = new HashSet<string>();

        var stores = await _storeService.GetAllStoresAsync();

        foreach (var store in stores)
        {
            List<DeliverySlotDto> slots;
            try
            {
                var orderSettings = await _settingService.LoadSettingAsync<OrderSettings>(store.Id);
                slots = ParseDeliverySlots(orderSettings.ScheduleDate);
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Rate reminder reconciler: failed to read delivery slots for store {store.Id}", e);
                continue;
            }

            foreach (var slot in slots.Where(s => s.IsEnabled))
            {
                if (!TimeSpan.TryParse(slot.DeliveryTime, out var deliveryTimeLocal))
                    continue;

                var jobId = $"{RECURRING_JOB_ID_PREFIX}{store.Id}-{deliveryTimeLocal:hhmm}";
                desiredJobIds.Add(jobId);

                var cron = BuildRateReminderCronUtc(deliveryTimeLocal);

                _recurringJobManager.AddOrUpdate<RateReminderJob>(jobId,
                    job => job.RunForSlotAsync(store.Id, slot.DeliveryTime), cron);
            }
        }

        RemoveStaleJobs(desiredJobIds);
    }

    /// <summary>
    /// Cron (UTC) for exactly 1 hour after a local delivery time, handling day rollover
    /// (e.g. a 23:30 local slot reminds at 00:30 local the next day - which is still the correct
    /// wall-clock instant, cron doesn't need a "next day" concept).
    /// </summary>
    private static string BuildRateReminderCronUtc(TimeSpan deliveryTimeLocal)
    {
        var reminderLocal = deliveryTimeLocal + TimeSpan.FromHours(1);
        if (reminderLocal >= TimeSpan.FromDays(1))
            reminderLocal -= TimeSpan.FromDays(1);

        var reminderUtc = reminderLocal - TimeSpan.FromHours(LOCAL_UTC_OFFSET_HOURS);
        if (reminderUtc < TimeSpan.Zero)
            reminderUtc += TimeSpan.FromDays(1);

        return $"{reminderUtc.Minutes} {reminderUtc.Hours} * * *";
    }

    private List<DeliverySlotDto> ParseDeliverySlots(string scheduleDateRaw)
    {
        if (string.IsNullOrWhiteSpace(scheduleDateRaw) || !scheduleDateRaw.TrimStart().StartsWith("["))
            return new List<DeliverySlotDto>();

        return JsonSerializer.Deserialize<List<DeliverySlotDto>>(scheduleDateRaw, JsonOptions)
               ?? new List<DeliverySlotDto>();
    }

    private void RemoveStaleJobs(HashSet<string> desiredJobIds)
    {
        var existingJobIds = JobStorage.Current.GetConnection().GetRecurringJobs()
            .Select(j => j.Id)
            .Where(id => id.StartsWith(RECURRING_JOB_ID_PREFIX, StringComparison.Ordinal));

        foreach (var staleId in existingJobIds.Except(desiredJobIds))
            _recurringJobManager.RemoveIfExists(staleId);
    }
}
