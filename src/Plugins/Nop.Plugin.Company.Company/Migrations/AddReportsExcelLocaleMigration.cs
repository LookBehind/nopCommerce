using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Seeds the "Download as Excel" reporting locale for existing installs (the
    /// earlier AddReportingLocales migration had already applied without this key).
    /// Idempotent; per language. (Fresh installs get it from AddReportingLocales.)
    /// </summary>
    [NopMigration("2026/06/14 12:10:00:0000000", "Company.AddReportsExcelLocale")]
    public class AddReportsExcelLocaleMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddReportsExcelLocaleMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            const string name = "Company.Reports.DownloadExcel";
            const string value = "Download as Excel";

            var languages = _dataProvider.GetTable<Language>().ToList();
            var existing = _dataProvider.GetTable<LocaleStringResource>();

            foreach (var lang in languages)
            {
                if (existing.Any(r => r.LanguageId == lang.Id && r.ResourceName == name))
                    continue;

                _dataProvider.InsertEntityAsync(new LocaleStringResource
                {
                    LanguageId = lang.Id,
                    ResourceName = name,
                    ResourceValue = value
                }).GetAwaiter().GetResult();
            }
        }

        public override void Down()
        {
        }
    }
}
