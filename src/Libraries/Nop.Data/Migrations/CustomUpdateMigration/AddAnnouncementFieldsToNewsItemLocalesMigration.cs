using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data.Migrations;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds the admin locale resources for News items' new Background color/Icon/Sort
    /// order fields (mobile-v2's Home announcement carousel now reads published NewsItems
    /// instead of a hardcoded mock - see AnnouncementV2ApiController). These three fields
    /// are deliberately NOT new NewsItem columns - they're stored as GenericAttributes
    /// (NewsController/NewsModelFactory), so this migration only needs to seed labels, no
    /// schema change and no cross-provider (SQL Server prod / Postgres dev) risk.
    /// </summary>
    [NopMigration("2026-09-25 00:00:00:0000000", "News.AddAnnouncementFieldsLocales", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class AddAnnouncementFieldsToNewsItemLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddAnnouncementFieldsToNewsItemLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new (string Key, string En, string Hy)[]
            {
                ("Admin.ContentManagement.News.NewsItems.Fields.Bg",
                    "Card background color",
                    "Քարտի ֆոնի գույնը"),
                ("Admin.ContentManagement.News.NewsItems.Fields.Bg.Hint",
                    "Hex color (e.g. #FDECC8) used behind this item's card in the mobile app's Home announcement carousel. Leave blank to use a default color.",
                    "Հեքս գույն (օր.՝ #FDECC8), որն օգտագործվում է բջջային հավելվածի Home էջի հայտարարությունների շարքում այս քարտի ֆոնին։ Դատարկ թողեք լռելյայն գույնն օգտագործելու համար։"),
                ("Admin.ContentManagement.News.NewsItems.Fields.Icon",
                    "Card icon (emoji)",
                    "Քարտի պատկերակը (էմոջի)"),
                ("Admin.ContentManagement.News.NewsItems.Fields.Icon.Hint",
                    "A single emoji shown on this item's card in the mobile app's Home announcement carousel. Leave blank to use a default icon.",
                    "Մեկ էմոջի, որը ցուցադրվում է բջջային հավելվածի Home էջի հայտարարությունների շարքում այս քարտին։ Դատարկ թողեք լռելյայն պատկերակն օգտագործելու համար։"),
                ("Admin.ContentManagement.News.NewsItems.Fields.SortOrder",
                    "Card sort order",
                    "Քարտի դասավորության կարգը"),
                ("Admin.ContentManagement.News.NewsItems.Fields.SortOrder.Hint",
                    "Controls this item's position in the mobile app's Home announcement carousel (lower shows first). Leave at 0 to fall back to newest-first.",
                    "Կառավարում է այս տարրի դիրքը բջջային հավելվածի Home էջի հայտարարությունների շարքում (փոքր արժեքն ավելի առաջ է ցուցադրվում)։ Թողեք 0՝ ամենավերջինը-առաջինը կարգով դասավորելու համար։"),
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
        }
    }
}
