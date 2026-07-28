using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Logging;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

[NopMigration("2026-07-29 00:00:00:0000001", "Notifications.Manager - Add PublicStore.PushNotificationSent activity log type")]
[SkipMigrationOnInstall]
public class PushNotificationSentActivityLogTypeMigration : Migration
{
    private const string SYSTEM_KEYWORD = "PublicStore.PushNotificationSent";

    private readonly INopDataProvider _dataProvider;

    public PushNotificationSentActivityLogTypeMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var typeExists = _dataProvider.GetTable<ActivityLogType>()
            .Any(t => t.SystemKeyword == SYSTEM_KEYWORD);

        if (!typeExists)
        {
            _dataProvider.InsertEntityAsync(new ActivityLogType
            {
                SystemKeyword = SYSTEM_KEYWORD,
                Enabled = true,
                Name = "Public store. Push notification sent"
            }).GetAwaiter().GetResult();
        }
    }

    public override void Down()
    {
    }
}
