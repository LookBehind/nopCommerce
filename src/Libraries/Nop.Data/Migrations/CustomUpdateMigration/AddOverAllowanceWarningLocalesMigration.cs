using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds the locale string resources for the mobile checkout over-allowance warning
    /// (Mobile.Checkout.OverAllowance.*). The server (OrderApiController.check-products)
    /// returns the resolved message so the client renders it rather than hard-coding the
    /// text - the wording differs by whether card self-pay is an active payment method.
    /// Seeds English defaults plus Armenian translations (tenants run English + Armenian,
    /// the Armenian language uses UniqueSeoCode "am"). Idempotent - only inserts keys not
    /// already present for a given language, so it is safe on install AND upgrade.
    /// </summary>
    [NopMigration("2026-07-27 12:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    public class AddOverAllowanceWarningLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddOverAllowanceWarningLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Mobile.Checkout.OverAllowance.WithSelfPay"] =
                    ("Order exceeds allowance, you will be redirected for payment",
                     "Պատվերը գերազանցում է սահմանաչափը, դուք կուղղորդվեք վճարման"),
                ["Mobile.Checkout.OverAllowance.NoSelfPay"] =
                    ("Order exceeds your remaining allowance",
                     "Պատվերը գերազանցում է ձեր մնացած սահմանաչափը")
            };

            var languages = _dataProvider.GetTable<Language>().ToList();
            var existing = _dataProvider.GetTable<LocaleStringResource>();

            foreach (var lang in languages)
            {
                var isArmenian =
                    string.Equals(lang.UniqueSeoCode, "am", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lang.UniqueSeoCode, "hy", StringComparison.OrdinalIgnoreCase) ||
                    (lang.Name != null && lang.Name.IndexOf("Armenian", StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var kv in resources)
                {
                    var present = existing.Any(r => r.LanguageId == lang.Id && r.ResourceName == kv.Key);
                    if (present)
                        continue;

                    _dataProvider.InsertEntityAsync(new LocaleStringResource
                    {
                        LanguageId = lang.Id,
                        ResourceName = kv.Key,
                        ResourceValue = isArmenian ? kv.Value.Hy : kv.Value.En
                    }).GetAwaiter().GetResult();
                }
            }
        }

        public override void Down()
        {
            // Data migration - no rollback.
        }
    }
}
