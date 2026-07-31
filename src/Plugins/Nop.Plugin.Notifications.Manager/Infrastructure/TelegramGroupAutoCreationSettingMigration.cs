using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Configuration;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

[NopMigration("2026-07-31 00:00:00:0000000", "Notifications.Manager - Add TelegramGroupAutoCreationEnabled setting")]
[SkipMigrationOnInstall]
public class TelegramGroupAutoCreationSettingMigration : Migration
{
    private const string SETTING_NAME = "notificationmanagersettings.telegramgroupautocreationenabled";

    private readonly INopDataProvider _dataProvider;

    public TelegramGroupAutoCreationSettingMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        var settingExists = _dataProvider.GetTable<Setting>()
            .Any(s => s.Name == SETTING_NAME && s.StoreId == 0);

        if (!settingExists)
        {
            _dataProvider.InsertEntityAsync(new Setting
            {
                Name = SETTING_NAME,
                Value = "false",
                StoreId = 0
            }).GetAwaiter().GetResult();
        }
    }

    public override void Down()
    {
    }
}
