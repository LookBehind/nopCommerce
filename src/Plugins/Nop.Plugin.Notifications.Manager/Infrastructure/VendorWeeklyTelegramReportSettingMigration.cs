using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Configuration;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

/// <summary>
/// Seeds the (blank, disabled-by-default) settings backing <see cref="ScheduledTasks.VendorWeeklyTelegramReportTask"/>
/// for tenants that already have this plugin installed. A fresh install picks these up automatically
/// via the standard settings installer, so this only matters on upgrade.
/// </summary>
[NopMigration("2026-09-11 00:00:00:0000000", "Notifications.Manager - Add vendor weekly Telegram report settings")]
[SkipMigrationOnInstall]
public class VendorWeeklyTelegramReportSettingMigration : Migration
{
    private static readonly string[] SETTING_NAMES =
    {
        "notificationmanagersettings.telegramreportbottoken",
        "notificationmanagersettings.telegramreportchatid"
    };

    private readonly INopDataProvider _dataProvider;

    public VendorWeeklyTelegramReportSettingMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        foreach (var settingName in SETTING_NAMES)
        {
            var settingExists = _dataProvider.GetTable<Setting>()
                .Any(s => s.Name == settingName && s.StoreId == 0);

            if (!settingExists)
            {
                _dataProvider.InsertEntityAsync(new Setting
                {
                    Name = settingName,
                    Value = string.Empty,
                    StoreId = 0
                }).GetAwaiter().GetResult();
            }
        }
    }

    public override void Down()
    {
    }
}
