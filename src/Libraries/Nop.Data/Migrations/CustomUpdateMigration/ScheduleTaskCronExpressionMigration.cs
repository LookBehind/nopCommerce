using FluentMigrator;
using Nop.Core.Domain.Tasks;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Adds the nullable <see cref="ScheduleTask.CronExpression"/> column to existing installs. Fresh installs
    /// get the column from <c>ScheduleTaskBuilder</c>, so this is skipped on install. Idempotent.
    /// See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    [NopMigration("2026-07-22 00:00:01:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class ScheduleTaskCronExpressionMigration : Migration
    {
        public override void Up()
        {
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(ScheduleTask)))
                    .Column(nameof(ScheduleTask.CronExpression))
                    .Exists())
            {
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(ScheduleTask)))
                    .AddColumn(nameof(ScheduleTask.CronExpression)).AsString(100).Nullable();
            }
        }

        public override void Down()
        {
            // no rollback
        }
    }
}
