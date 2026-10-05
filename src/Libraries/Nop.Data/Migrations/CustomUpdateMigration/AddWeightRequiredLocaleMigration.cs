using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds the validation message shown when a new (shippable) product is saved
    /// without a weight - see ProductController.Create. English only, following
    /// the same admin-is-English-only precedent as the other product-edit locale
    /// migrations. Idempotent - only inserts a key for a language that doesn't
    /// already have it, safe on install and upgrade. A separate migration (not an
    /// addition to AddIngredientsRequiredLocalesMigration) because that one has
    /// already been applied on dev, so its Up() won't re-run.
    /// </summary>
    [NopMigration("2026-10-05 13:00:00:0000000", "Catalog.AddWeightRequiredLocale", UpdateMigrationType.Localization)]
    public class AddWeightRequiredLocaleMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddWeightRequiredLocaleMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, string>
            {
                ["Admin.Catalog.Products.Fields.Weight.Required"] = "Weight is required for new products."
            };

            var languages = _dataProvider.GetTable<Language>().ToList();
            var existing = _dataProvider.GetTable<LocaleStringResource>();

            foreach (var lang in languages)
            {
                foreach (var kv in resources)
                {
                    var present = existing.Any(r => r.LanguageId == lang.Id && r.ResourceName == kv.Key);
                    if (present)
                        continue;

                    _dataProvider.InsertEntityAsync(new LocaleStringResource
                    {
                        LanguageId = lang.Id,
                        ResourceName = kv.Key,
                        ResourceValue = kv.Value
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
