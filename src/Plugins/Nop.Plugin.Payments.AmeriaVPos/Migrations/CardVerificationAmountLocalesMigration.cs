using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Payments.AmeriaVPos.Migrations
{
    /// <summary>
    /// Seeds the locale resource for the new Configure page "Card verification amount"
    /// field on environments where the plugin is already installed (InstallAsync only
    /// runs on a fresh install, not on upgrade). English only, matching this admin UI's
    /// existing no-Armenian-seeding precedent. Idempotent.
    /// </summary>
    [NopMigration("2026-09-24 18:05:00:0000000", "AmeriaVPos.CardVerificationAmountLocales", UpdateMigrationType.Localization)]
    public class CardVerificationAmountLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public CardVerificationAmountLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, string>
            {
                ["Plugins.Payments.AmeriaVPos.Fields.CardVerificationAmount"] = "Card verification amount",
                ["Plugins.Payments.AmeriaVPos.Fields.CardVerificationAmount.Hint"] =
                    "Nominal amount charged (then immediately refunded) to verify and bind a new card - the vPOS API has no $0 verify-only call. Defaults to 10 AMD to match the sandbox's fixed test-charge restriction."
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
