using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds locale string resources for order-item-scoped product review rejection messages
    /// (mobile + storefront): a duplicate submission for an already-reviewed order item, and a
    /// submission against a cancelled order. Seeds English defaults plus Armenian translations.
    /// Idempotent - only inserts keys not already present for a given language, so it is safe on
    /// install AND upgrade.
    /// </summary>
    [NopMigration("2026-09-01 00:00:01:0000000", "4.60.0", UpdateMigrationType.Data)]
    public class AddOrderItemReviewLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddOrderItemReviewLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Reviews.OrderItemAlreadyReviewed"] =
                    ("You have already reviewed this item",
                     "Դուք արդեն գնահատել եք այս ապրանքը"),
                ["Reviews.CannotReviewCancelledOrder"] =
                    ("This order was cancelled and can no longer be reviewed",
                     "Այս պատվերը չեղարկվել է և այլևս հնարավոր չէ գնահատել")
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
