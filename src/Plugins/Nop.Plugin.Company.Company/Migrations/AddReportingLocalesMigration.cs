using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Seeds locale string resources for the reporting UI (header link, page title) for
    /// every installed language with a default value (admins can translate per language
    /// in the admin UI). Idempotent; runs on install AND upgrade so existing tenants get
    /// the strings. Uses INopDataProvider raw inserts (same pattern as the role migration).
    /// </summary>
    [NopMigration("2026/06/14 12:05:00:0000000", "Company.AddReportingLocales")]
    public class AddReportingLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddReportingLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, string>
            {
                ["Company.Reports.HeaderLink"] = "Reports",
                ["Company.Reports.Title"] = "Reports",
                ["Company.Reports.DownloadExcel"] = "Download as Excel"
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
