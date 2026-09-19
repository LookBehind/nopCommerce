using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Data.Migrations;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds a single "Ingredients" specification attribute with the mobile-v2 mock's
    /// 11-item taxonomy (Milk, Eggs, Fish, Shellfish, Tree Nuts, Peanuts, Wheat, Soybeans,
    /// Sesame, Gluten-Free, Sugar-Free) plus a new, distinct "Gluten" option - the actual
    /// fix for the "Wheat" proxy gap flagged in the mobile-v2 plan (gluten also lives in
    /// barley/rye, which "Wheat" doesn't cover). Staff assign these to products via the
    /// existing generic "Add specification attribute" UI on the product edit page - no new
    /// admin UI needed for that part. Idempotent by attribute name, so safe on install and
    /// upgrade; runs on install too (unlike the schema migration) since there's no other
    /// seed path for this data on a fresh database.
    /// </summary>
    [NopMigration("2026-09-19 09:05:00:0000000", "Catalog.SeedIngredientsSpecificationAttribute", UpdateMigrationType.Data)]
    public class SeedIngredientsSpecificationAttributeMigration : Migration
    {
        private const string AttributeName = "Ingredients";

        private readonly INopDataProvider _dataProvider;

        public SeedIngredientsSpecificationAttributeMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var existingAttribute = _dataProvider.GetTable<SpecificationAttribute>()
                .FirstOrDefault(sa => sa.Name == AttributeName);

            if (existingAttribute != null)
                return;

            var attribute = new SpecificationAttribute
            {
                Name = AttributeName,
                DisplayOrder = 0
            };
            _dataProvider.InsertEntityAsync(attribute).GetAwaiter().GetResult();

            //(Name, IsAllergen) - Gluten-Free/Sugar-Free are free-from product claims, not
            //allergens, matching the mock's own LABELS distinction; every other entry is a
            //real allergen, including the new "Gluten" option
            var options = new (string Name, bool IsAllergen)[]
            {
                ("Milk", true),
                ("Eggs", true),
                ("Fish", true),
                ("Shellfish", true),
                ("Tree Nuts", true),
                ("Peanuts", true),
                ("Wheat", true),
                ("Soybeans", true),
                ("Sesame", true),
                ("Gluten", true),
                ("Gluten-Free", false),
                ("Sugar-Free", false),
            };

            for (var i = 0; i < options.Length; i++)
            {
                _dataProvider.InsertEntityAsync(new SpecificationAttributeOption
                {
                    SpecificationAttributeId = attribute.Id,
                    Name = options[i].Name,
                    IsAllergen = options[i].IsAllergen,
                    DisplayOrder = i
                }).GetAwaiter().GetResult();
            }
        }

        public override void Down()
        {
            var attribute = _dataProvider.GetTable<SpecificationAttribute>()
                .FirstOrDefault(sa => sa.Name == AttributeName);

            if (attribute == null)
                return;

            var options = _dataProvider.GetTable<SpecificationAttributeOption>()
                .Where(o => o.SpecificationAttributeId == attribute.Id)
                .ToList();

            foreach (var option in options)
            {
                _dataProvider.DeleteEntityAsync(option).GetAwaiter().GetResult();
            }

            _dataProvider.DeleteEntityAsync(attribute).GetAwaiter().GetResult();
        }
    }
}
