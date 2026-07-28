using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Tasks;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

/// <summary>
/// Removes the legacy "Rate Reminder Notification Task" ScheduleTask row (40-min poll,
/// `RateRemainderNotificationTask`, note the historical typo). Superseded by the reconciler-driven
/// per-slot Hangfire jobs in <see cref="ScheduledTasks.RateReminderReconciler"/>, which aren't
/// `ScheduleTask` rows at all - same pattern as <see cref="RetireOrphanedPreDeliveryReminderTaskMigration"/>.
/// See docs/plans/2026-07-29-rate-reminder-slot-jobs.md.
/// </summary>
[NopMigration("2026-07-29 00:00:00:0000003", "Notifications.Manager - Retire legacy RateRemainderNotificationTask ScheduleTask row")]
[SkipMigrationOnInstall]
public class RetireLegacyRateReminderTaskMigration : Migration
{
    private const string LEGACY_TASK_TYPE_NAME =
        "Nop.Plugin.Notifications.Manager.ScheduledTasks.RateRemainderNotificationTask";

    private readonly INopDataProvider _dataProvider;

    public RetireLegacyRateReminderTaskMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var legacyTask = _dataProvider.GetTable<ScheduleTask>()
            .FirstOrDefault(t => t.Type == LEGACY_TASK_TYPE_NAME);

        if (legacyTask != null)
            _dataProvider.DeleteEntityAsync(legacyTask).GetAwaiter().GetResult();
    }

    public override void Down()
    {
    }
}
