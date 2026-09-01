using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds locale string resources for the storefront order-item-review UI: the order-item
    /// picker shown on the product review page when a customer has multiple eligible orders to
    /// review, and the "write a review" entry point added to the order details page. Seeds
    /// English defaults plus Armenian translations. Idempotent - only inserts keys not already
    /// present for a given language, so it is safe on install AND upgrade.
    /// </summary>
    [NopMigration("2026-09-01 00:00:02:0000000", "4.60.0", UpdateMigrationType.Data)]
    public class AddStorefrontOrderItemReviewUiLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddStorefrontOrderItemReviewUiLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Reviews.SelectOrderToReview"] =
                    ("Which order would you like to review?",
                     "Ո՞ր պատվերը եք ցանկանում գնահատել"),
                ["Reviews.SelectOrderOption"] =
                    ("Order #{0} ({1})",
                     "Պատվեր #{0} ({1})"),
                ["Order.Product(s).WriteAReview"] =
                    ("Write a review",
                     "Գրել գնահատական")
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
