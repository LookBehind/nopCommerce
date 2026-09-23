using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds locale string resources for the product edit page's new "Ingredients"
    /// tab (a plain checkbox list over the real "Ingredients" specification
    /// attribute's options - see ProductModelFactory.PrepareProductIngredientsModelAsync/
    /// ProductController.SaveIngredientMappingsAsync). English only - unlike
    /// customer/storefront-facing strings, this admin's own UI has no existing
    /// Armenian resource seeding anywhere in this codebase (confirmed by grep), so
    /// this follows that same admin-is-English-only precedent rather than
    /// introducing translation for just this one tab. Idempotent - only inserts a
    /// key for a language that doesn't already have it, safe on install and upgrade.
    /// </summary>
    [NopMigration("2026-09-23 15:00:00:0000000", "Catalog.AddProductIngredientsTabLocales", UpdateMigrationType.Localization)]
    public class AddProductIngredientsTabLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddProductIngredientsTabLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, string>
            {
                ["Admin.Catalog.Products.Ingredients"] = "Ingredients",
                ["Admin.Catalog.Products.Ingredients.Allergen"] = "Allergen",
                ["Admin.Catalog.Products.Ingredients.NoOptionsConfigured"] =
                    "No ingredient options are configured yet. Add them under Catalog > Attributes > Specification attributes > Ingredients.",
                ["Admin.Catalog.Products.Ingredients.Fields.SelectedIngredientOptionIds"] = "Ingredients"
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
