using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Seeds the locale string resource for the new "search by customer name/email" filter on
    /// the admin Support Cases list. Separate from AddSupportLocalesMigration since that one
    /// already ran on install/upgrade and won't run again just because new keys were added to
    /// its dictionary - each new batch of keys needs its own migration.
    /// </summary>
    [NopMigration("2026/09/27 09:00:00:0000000", "Company.Support.AddCustomerSearchLocale")]
    public class AddSupportCustomerSearchLocaleMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddSupportCustomerSearchLocaleMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Admin.Support.Cases.List.SearchCustomer"] = ("Customer name or email", "Հաճախորդի անուն կամ էլ. փոստ")
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
            // No rollback for seeded locale resources.
        }
    }
}
