using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Seeds the "Products.VendorUnavailable" locale resource shown on a product's detail
    /// page when its vendor is off/non-working for the effective delivery date - the same
    /// notice pattern as core's "Products.Discontinued" for unpublished products, since a
    /// direct link to the product bypasses the catalog listing's vendor-schedule filtering.
    /// Idempotent - only inserts keys not already present for a given language.
    /// </summary>
    [NopMigration("2026/09/16 12:00:00:0000000", "Company.AddVendorUnavailableProductLocale")]
    public class AddVendorUnavailableProductLocaleMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddVendorUnavailableProductLocaleMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Products.VendorUnavailable"] =
                    ("Sorry, this product is currently unavailable for delivery.",
                     "Կներեք, այս ապրանքը այս պահին հասանելի չէ առաքման համար։")
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
