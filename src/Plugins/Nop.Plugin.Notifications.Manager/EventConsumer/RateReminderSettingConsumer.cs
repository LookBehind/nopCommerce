using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Configuration;
using Nop.Core.Events;
using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Services.Events;
using Nop.Services.Logging;

namespace Nop.Plugin.Notifications.Manager.EventConsumer;

/// <summary>
/// Reconciles the rate-reminder Hangfire jobs whenever the store's delivery-slot configuration
/// (`ordersettings.scheduledate`) changes - "reconfigure only when timing is updated" per
/// docs/plans/2026-07-29-rate-reminder-slot-jobs.md §4.3. Auto-discovered by nopCommerce's
/// IConsumer&lt;&gt; assembly scan, no manual registration needed. Coexists with
/// <see cref="PreDeliveryNudgeSettingConsumer"/> listening to the same setting name -
/// nopCommerce dispatches the event to every registered consumer.
/// </summary>
public class RateReminderSettingConsumer : IConsumer<EntityUpdatedEvent<Setting>>
{
    private const string SCHEDULE_DATE_SETTING_NAME = "ordersettings.scheduledate";

    private readonly RateReminderReconciler _reconciler;
    private readonly ILogger _logger;

    public RateReminderSettingConsumer(RateReminderReconciler reconciler, ILogger logger)
    {
        _reconciler = reconciler;
        _logger = logger;
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Setting> eventMessage)
    {
        var settingName = eventMessage.Entity.Name;
        if (!string.Equals(settingName, SCHEDULE_DATE_SETTING_NAME, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await _reconciler.ReconcileAsync();
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync("Rate reminder reconciler failed after a delivery-slot setting change", e);
        }
    }
}
