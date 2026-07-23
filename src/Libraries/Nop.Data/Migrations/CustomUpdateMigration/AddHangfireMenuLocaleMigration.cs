using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Seeds the locale string resources for the dynamic-scheduled-tasks feature: the "Hangfire" admin menu
    /// item under System (Areas/Admin/sitemap.config) and the CRON-expression column on the Schedule Tasks
    /// grid. Seeds English defaults plus Armenian translations (tenants run English + Armenian, UniqueSeoCode
    /// "am"). Idempotent - only inserts the key for a language when it is not already present, so it is safe on
    /// install AND upgrade.
    /// </summary>
    [NopMigration("2026-07-22 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    public class AddHangfireMenuLocaleMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddHangfireMenuLocaleMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Admin.System.Hangfire"] =
                    ("Background jobs (Hangfire)",
                     "Ֆոնային առաջադրանքներ (Hangfire)"),
                ["Admin.System.ScheduleTasks.CronExpression"] =
                    ("CRON expression",
                     "CRON արտահայտություն"),
                ["Admin.System.ScheduleTasks.CronExpression.Hint"] =
                    ("Optional. When set (e.g. \"*/15 * * * *\"), the task runs on this CRON schedule via Hangfire and ignores the interval. Leave empty to use the interval (seconds).",
                     "Ընտրովի։ Երբ նշված է (օր․՝ \"*/15 * * * *\"), առաջադրանքը կատարվում է այս CRON ժամանակացույցով Hangfire-ի միջոցով՝ անտեսելով ինտերվալը։ Թողեք դատարկ՝ ինտերվալը (վայրկյաններ) օգտագործելու համար։"),
                ["Admin.System.ScheduleTasks.CronExpression.Invalid"] =
                    ("Invalid CRON expression: {0}",
                     "Անվավեր CRON արտահայտություն՝ {0}")
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
