using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds the locale string resources for the quick-select review-reason chips shown
    /// above the review comment box on mobile and storefront (Reviews.QuickOption.*).
    /// Seeds English defaults plus Armenian translations (the tenants run English +
    /// Armenian, the Armenian language has UniqueSeoCode "am"). Idempotent - only
    /// inserts keys not already present for a given language, so it is safe on install
    /// AND upgrade. Uses INopDataProvider raw inserts.
    /// </summary>
    [NopMigration("2026-07-09 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    public class AddReviewQuickOptionsLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddReviewQuickOptionsLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Reviews.QuickOption.ItemMismatch"] =
                    ("Item did not match the photo",
                     "Ապրանքը չի համապատասխանում նկարին"),
                ["Reviews.QuickOption.PortionTooSmall"] =
                    ("Portion too small",
                     "Չափաբաժինը շատ փոքր էր"),
                ["Reviews.QuickOption.PoorTaste"] =
                    ("Poor taste",
                     "Վատ համ"),
                ["Reviews.QuickOption.NoteNotFollowed"] =
                    ("My note was not followed",
                     "Իմ նշումը հաշվի չի առնվել"),
                ["Reviews.QuickOption.PackagingDamaged"] =
                    ("Packaging was damaged",
                     "Փաթեթավորումը վնասված էր"),
                ["Reviews.QuickOption.FoodSpilled"] =
                    ("Food spilled or leaked",
                     "Կերակուրը թափվել կամ արտահոսել էր"),
                ["Reviews.QuickOption.GreatTaste"] =
                    ("Great taste",
                     "Հիանալի համ")
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
