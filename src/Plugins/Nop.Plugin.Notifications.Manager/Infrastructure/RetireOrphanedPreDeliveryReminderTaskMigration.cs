using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Tasks;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

/// <summary>
/// Removes the orphaned "Pre-delivery reminder (45 min before)" ScheduleTask row. It was seeded by
/// <see cref="PreDeliveryReminderTaskMigration"/> for a `PreDeliveryReminderTask` class that never
/// shipped (implemented only on an unmerged branch) - it has been logging a benign
/// "could not be resolved; skipping run" every 5 minutes ever since. Superseded by the
/// reconciler-driven per-slot Hangfire jobs in <see cref="ScheduledTasks.PreDeliveryNudgeReconciler"/>,
/// which aren't `ScheduleTask` rows at all. See docs/plans/2026-07-28-vendor-delivery-mini-app.md.
/// </summary>
[NopMigration("2026-07-28 00:00:00:0000001", "Notifications.Manager - Retire orphaned PreDeliveryReminderTask ScheduleTask row")]
[SkipMigrationOnInstall]
public class RetireOrphanedPreDeliveryReminderTaskMigration : Migration
{
    private const string ORPHANED_TASK_TYPE_NAME =
        "Nop.Plugin.Notifications.Manager.ScheduledTasks.PreDeliveryReminderTask";

    private readonly INopDataProvider _dataProvider;

    public RetireOrphanedPreDeliveryReminderTaskMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var orphanedTask = _dataProvider.GetTable<ScheduleTask>()
            .FirstOrDefault(t => t.Type == ORPHANED_TASK_TYPE_NAME);

        if (orphanedTask != null)
            _dataProvider.DeleteEntityAsync(orphanedTask).GetAwaiter().GetResult();
    }

    public override void Down()
    {
    }
}
