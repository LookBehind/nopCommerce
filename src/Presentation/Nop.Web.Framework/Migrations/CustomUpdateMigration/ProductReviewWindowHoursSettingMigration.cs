using FluentMigrator;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Services.Configuration;

namespace Nop.Web.Framework.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds CatalogSettings.ProductReviewWindowHours to 24 - the default cutoff for
    /// api/catalog/v2/add-product-reviews (0 = always allowed, admin-editable via
    /// Admin &gt; Configuration &gt; All settings). MySnacks addition.
    /// </summary>
    [NopMigration("2026-09-27 20:05:00:0000000", "4.60.0", UpdateMigrationType.Settings)]
    [SkipMigrationOnInstall]
    public class ProductReviewWindowHoursSettingMigration : MigrationBase
    {
        public override void Up()
        {
            if (!DataSettingsManager.IsDatabaseInstalled())
                return;

            //do not use DI, because it produces exception on the installation process
            var settingService = EngineContext.Current.Resolve<ISettingService>();
            settingService.SetSettingAsync("catalogsettings.productreviewwindowhours", 24).Wait();
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
