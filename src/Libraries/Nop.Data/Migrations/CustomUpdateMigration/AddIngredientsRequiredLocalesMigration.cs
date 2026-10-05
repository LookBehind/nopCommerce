using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds locale string resources for the "require an ingredient/allergen
    /// declaration when adding a product" gate on the product create page: the
    /// "Doesn't include any of the above" checkbox label and the validation
    /// message shown when neither an option nor that checkbox is selected (see
    /// ProductController.Create / _CreateOrUpdate.Ingredients.cshtml). English
    /// only, following the same admin-is-English-only precedent as
    /// AddProductIngredientsTabLocalesMigration. Idempotent - only inserts a key
    /// for a language that doesn't already have it, safe on install and upgrade.
    /// </summary>
    [NopMigration("2026-10-05 12:00:00:0000000", "Catalog.AddIngredientsRequiredLocales", UpdateMigrationType.Localization)]
    public class AddIngredientsRequiredLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddIngredientsRequiredLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, string>
            {
                ["Admin.Catalog.Products.Ingredients.Fields.NoIngredients"] = "Doesn't include any of the above",
                ["Admin.Catalog.Products.Ingredients.Required"] =
                    "Please select the ingredients/allergens this product contains, or tick \"Doesn't include any of the above\"."
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
