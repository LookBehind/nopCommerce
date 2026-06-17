using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Configuration;
using Nop.Core.Domain.Directory;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Configures AMD (Armenian Dram) to display without decimal places.
    /// AMD has no fractional subunit, so prices are shown as whole numbers with a
    /// thousands separator and the dram sign (e.g. "1,500 ֏").
    /// </summary>
    [NopMigration("2026-06-17 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class AmdNoDecimalsMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AmdNoDecimalsMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            //resolve AMD by currency code (never by id - ids differ per tenant db)
            var amdCurrencies = _dataProvider.GetTable<Currency>().ToList()
                .Where(currency => string.Equals(currency.CurrencyCode, "AMD", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var currency in amdCurrencies)
            {
                //whole number, grouped, with the dram sign (֏)
                currency.CustomFormatting = "#,##0 ֏";
                //snap computed prices to whole AMD so displayed line items sum correctly
                currency.RoundingType = RoundingType.Rounding1;
                _dataProvider.UpdateEntityAsync(currency).GetAwaiter().GetResult();
            }

            //the dram sign is already part of CustomFormatting - don't append the "(AMD)" label after it
            var labelSettings = _dataProvider.GetTable<Setting>().ToList()
                .Where(setting => string.Equals(setting.Name, "currencysettings.displaycurrencylabel", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(setting.Value, "False", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var setting in labelSettings)
            {
                setting.Value = "False";
                _dataProvider.UpdateEntityAsync(setting).GetAwaiter().GetResult();
            }
        }

        public override void Down()
        {
            //no automated downgrade - currency formatting is configuration data
        }
    }
}
