using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Adds the "is allergen" flag to specification attribute options, so an ingredient
    /// option (e.g. "Peanuts") can be marked as an allergen wherever it's assigned to a
    /// product. Fresh installs already get the column from
    /// <see cref="Nop.Data.Mapping.Builders.Catalog.SpecificationAttributeOptionBuilder"/>,
    /// so this migration is skipped on install and only needed for existing databases.
    /// Plain nullable-default boolean column via FluentMigrator's standard AddColumn - safe
    /// on both SQL Server (prod) and PostgreSQL (dev), no raw/provider-specific SQL.
    /// Also seeds the two new admin locale resources (EN + HY) needed for the option
    /// edit/create popup and options grid column.
    /// </summary>
    [NopMigration("2026-09-19 09:00:00:0000000", "Catalog.AddIsAllergenToSpecificationAttributeOption", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class AddIsAllergenToSpecificationAttributeOptionMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddIsAllergenToSpecificationAttributeOptionMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(SpecificationAttributeOption));

            if (!Schema.Table(tableName).Column(nameof(SpecificationAttributeOption.IsAllergen)).Exists())
            {
                Alter.Table(tableName)
                    .AddColumn(nameof(SpecificationAttributeOption.IsAllergen))
                    .AsBoolean()
                    .NotNullable()
                    .WithDefaultValue(false);
            }

            SeedLocaleResources();
        }

        private void SeedLocaleResources()
        {
            var resources = new (string Key, string En, string Hy)[]
            {
                ("Admin.Catalog.Attributes.SpecificationAttributes.SpecificationAttribute.Options.Fields.IsAllergen",
                    "Is allergen",
                    "Ալերգեն է"),
                ("Admin.Catalog.Attributes.SpecificationAttributes.SpecificationAttribute.Options.Fields.IsAllergen.Hint",
                    "Check if this option is a common allergen (e.g. Peanuts, Milk), as opposed to a plain ingredient or a free-from claim (e.g. Gluten-Free). Used to flag allergy conflicts for customers.",
                    "Նշեք, եթե այս տարբերակը տարածված ալերգեն է (օր. գետնանուշ, կաթ), այլ ոչ թե պարզապես բաղադրիչ կամ \"զերծ է\" հայտարարություն (օր. Առանց գլյուտենի)։ Օգտագործվում է հաճախորդների համար ալերգիայի հակասությունները նշելու համար։"),
            };

            var languages = _dataProvider.GetTable<Language>().ToList();
            var existing = _dataProvider.GetTable<LocaleStringResource>();

            foreach (var lang in languages)
            {
                var isArmenian =
                    string.Equals(lang.UniqueSeoCode, "am", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lang.UniqueSeoCode, "hy", System.StringComparison.OrdinalIgnoreCase) ||
                    (lang.Name != null && lang.Name.IndexOf("Armenian", System.StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var resource in resources)
                {
                    var present = existing.Any(r => r.LanguageId == lang.Id && r.ResourceName == resource.Key);
                    if (present)
                        continue;

                    _dataProvider.InsertEntityAsync(new LocaleStringResource
                    {
                        LanguageId = lang.Id,
                        ResourceName = resource.Key,
                        ResourceValue = isArmenian ? resource.Hy : resource.En
                    }).GetAwaiter().GetResult();
                }
            }
        }

        public override void Down()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(SpecificationAttributeOption));

            if (Schema.Table(tableName).Column(nameof(SpecificationAttributeOption.IsAllergen)).Exists())
            {
                Delete.Column(nameof(SpecificationAttributeOption.IsAllergen)).FromTable(tableName);
            }
        }
    }
}
