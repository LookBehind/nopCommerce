using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using LinqToDB;
using Nop.Core.Domain.Tasks;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Migrates existing fixed-interval schedule tasks onto the dynamic Hangfire scheduler by deriving a CRON
    /// expression from each task's Seconds interval. Only tasks whose interval maps CLEANLY onto a standard CRON
    /// (clock-aligned: whole minutes that divide 60, whole hours that divide 24, or daily) are converted; odd
    /// intervals (e.g. 40 min) and sub-minute intervals are left on the legacy timer. Only rows that do not yet
    /// have a CronExpression are touched, so the reminder task (already set to "*/15 * * * *") is untouched and
    /// re-running is safe. Once a task has a CronExpression, TaskManager skips it and Hangfire owns it.
    /// See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    [NopMigration("2026-07-23 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class MigrateScheduleTasksToCronMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public MigrateScheduleTasksToCronMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        /// <summary>
        /// Converts a fixed interval (seconds) into a clock-aligned standard 5-field CRON expression.
        /// Returns null when the interval does not map cleanly (leave such tasks on the legacy timer).
        /// Sub-minute intervals are intentionally not converted (Hangfire's scheduler polls ~every 15s, so
        /// sub-minute CRON would not fire reliably).
        /// </summary>
        public static string SecondsToCron(int seconds)
        {
            if (seconds < 60)
                return null;
            if (seconds % 60 != 0)
                return null;

            var minutes = seconds / 60;

            if (minutes == 1)
                return "* * * * *";
            if (minutes < 60)
                return 60 % minutes == 0 ? $"*/{minutes} * * * *" : null;
            if (minutes % 60 != 0)
                return null;

            var hours = minutes / 60;

            if (hours == 1)
                return "0 * * * *";
            if (hours < 24)
                return 24 % hours == 0 ? $"0 */{hours} * * *" : null;
            if (hours % 24 != 0)
                return null;

            //whole number of days -> only a single-day cadence maps to a simple daily CRON
            return hours / 24 == 1 ? "0 0 * * *" : null;
        }

        public override void Up()
        {
            var table = _dataProvider.GetTable<ScheduleTask>();

            //only tasks not already owned by Hangfire (no CronExpression yet)
            var tasks = table
                .Where(t => t.CronExpression == null || t.CronExpression == string.Empty)
                .ToList();

            foreach (var task in tasks)
            {
                var cron = SecondsToCron(task.Seconds);
                if (string.IsNullOrEmpty(cron))
                    continue; //odd/sub-minute interval: leave on the legacy timer

                table
                    .Where(t => t.Id == task.Id)
                    .Set(t => t.CronExpression, cron)
                    .Update();
            }
        }

        public override void Down()
        {
            // no rollback
        }
    }
}
