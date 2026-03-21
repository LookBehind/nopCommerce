using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Tasks;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

[NopMigration("2026-02-20 00:00:00:0000001", "Notifications.Manager - Add PreDeliveryReminderTask to ScheduleTask table")]
[SkipMigrationOnInstall]
public class PreDeliveryReminderTaskMigration : Migration
{
    private const string TASK_TYPE_NAME =
        "Nop.Plugin.Notifications.Manager.ScheduledTasks.PreDeliveryReminderTask";
    private const string TASK_FRIENDLY_NAME = "Pre-delivery reminder (45 min before)";

    private readonly INopDataProvider _dataProvider;

    public PreDeliveryReminderTaskMigration(INopDataProvider dataProvider)
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
                Seconds = 300,
                StopOnError = false
            }).GetAwaiter().GetResult();
        }
    }

    public override void Down()
    {
    }
}
