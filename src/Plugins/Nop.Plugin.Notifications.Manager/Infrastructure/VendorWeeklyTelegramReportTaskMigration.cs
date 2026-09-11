using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Tasks;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

/// <summary>
/// Registers <see cref="ScheduledTasks.VendorWeeklyTelegramReportTask"/> as a Hangfire recurring job,
/// replacing the external Redash-driven "telegram-report-*" k8s CronJob for this tenant. Cron is in
/// UTC (Hangfire's AddOrUpdate default, see HangfireRecurringTaskRegistrar) - "0 14 * * *" = 18:00
/// Armenia time (UTC+4, no DST), matching the CronJob's "0 18 * * * Asia/Yerevan" schedule. Enabled by
/// default but harmless out of the box: the task no-ops until TelegramReportBotToken/ChatId are set
/// via Admin -> Configure (see docs/plans/2026-06-14-customer-dashboards-off-redash-impl-plan.md,
/// Phase 5).
/// </summary>
[NopMigration("2026-09-11 00:00:00:0000001", "Notifications.Manager - Add VendorWeeklyTelegramReportTask to ScheduleTask table")]
[SkipMigrationOnInstall]
public class VendorWeeklyTelegramReportTaskMigration : Migration
{
    private const string TASK_TYPE_NAME =
        "Nop.Plugin.Notifications.Manager.ScheduledTasks.VendorWeeklyTelegramReportTask";
    private const string TASK_FRIENDLY_NAME = "Vendor weekly Telegram report";

    private readonly INopDataProvider _dataProvider;

    public VendorWeeklyTelegramReportTaskMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var taskExists = _dataProvider.GetTable<ScheduleTask>()
            .Any(t => t.Type == TASK_TYPE_NAME);

        if (!taskExists)
        {
            _dataProvider.InsertEntityAsync(new ScheduleTask
            {
                Enabled = true,
                Name = TASK_FRIENDLY_NAME,
                Type = TASK_TYPE_NAME,
                Seconds = 86400,
                StopOnError = false,
                CronExpression = "0 14 * * *"
            }).GetAwaiter().GetResult();
        }
    }

    public override void Down()
    {
    }
}
