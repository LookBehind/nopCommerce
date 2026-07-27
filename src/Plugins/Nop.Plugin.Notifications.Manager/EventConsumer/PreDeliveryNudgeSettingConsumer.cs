using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Configuration;
using Nop.Core.Events;
using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Services.Events;
using Nop.Services.Logging;

namespace Nop.Plugin.Notifications.Manager.EventConsumer;

/// <summary>
/// Reconciles the pre-delivery-nudge Hangfire jobs whenever the store's delivery-slot
/// configuration (`ordersettings.scheduledate`) changes - "reconfigure only when timing is
/// updated" per docs/plans/2026-07-28-vendor-delivery-mini-app.md §4.1. Auto-discovered by
/// nopCommerce's IConsumer&lt;&gt; assembly scan, no manual registration needed.
/// </summary>
public class PreDeliveryNudgeSettingConsumer : IConsumer<EntityUpdatedEvent<Setting>>
{
    private const string SCHEDULE_DATE_SETTING_NAME = "ordersettings.scheduledate";

    private readonly PreDeliveryNudgeReconciler _reconciler;
    private readonly ILogger _logger;

    public PreDeliveryNudgeSettingConsumer(PreDeliveryNudgeReconciler reconciler, ILogger logger)
    {
        _reconciler = reconciler;
        _logger = logger;
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Setting> eventMessage)
    {
        if (!string.Equals(eventMessage.Entity.Name, SCHEDULE_DATE_SETTING_NAME, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await _reconciler.ReconcileAsync();
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync("Pre-delivery nudge reconciler failed after a delivery-slot setting change", e);
        }
    }
}
