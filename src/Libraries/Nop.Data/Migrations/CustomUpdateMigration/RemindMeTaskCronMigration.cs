using System.Linq;
using FluentMigrator;
using LinqToDB;
using Nop.Core.Domain.Tasks;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Switches the "Remind Me Notification Task" onto the dynamic Hangfire scheduler: it gets a 15-minute CRON
    /// so the reminder dispatcher buckets customers by their chosen 15-minute slot. Seconds is lowered to 60 so
    /// the legacy interval-gate in Task.ExecuteAsync always passes when Hangfire triggers the task via the
    /// self-POST path (the task no longer runs on the legacy timer once a CRON is set - see TaskManager).
    /// Idempotent (re-running just re-sets the same values). See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    [NopMigration("2026-07-22 00:00:03:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class RemindMeTaskCronMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public RemindMeTaskCronMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            const string taskType = "Nop.Plugin.Notifications.Manager.ScheduledTasks.RemindMeNotificationTask";

            _dataProvider.GetTable<ScheduleTask>()
                .Where(st => st.Type == taskType)
                .Set(st => st.CronExpression, "*/15 * * * *")
                .Set(st => st.Seconds, 60)
                .Update();
        }

        public override void Down()
        {
            // no rollback
        }
    }
}
