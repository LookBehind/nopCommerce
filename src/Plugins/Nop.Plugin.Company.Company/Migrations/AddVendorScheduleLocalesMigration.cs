using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Seeds the locale string resources used by the new Admin company-vendor schedule
    /// popup (weekly working days + day-off calendar) and the checkout-time vendor
    /// availability error. Seeds English defaults plus Armenian translations. Idempotent
    /// — only inserts keys not already present for a given language, so it is safe on
    /// install AND upgrade.
    /// </summary>
    [NopMigration("2026/08/02 10:05:00:0000000", "Company.AddVendorScheduleLocales")]
    public class AddVendorScheduleLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddVendorScheduleLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Admin.Companies.Company.Vendors.Schedule"] =
                    ("Schedule",
                     "Ժամանակացույց"),
                ["Admin.Companies.Company.Vendors.Schedule.WorkingDays"] =
                    ("Working days",
                     "Աշխատանքային օրեր"),
                ["Admin.Companies.Company.Vendors.Schedule.WorkingDays.Hint"] =
                    ("Select the days of week this vendor works for this company. If none are selected, the vendor is available every day.",
                     "Ընտրեք շաբաթվա օրերը, երբ այս մատակարարը աշխատում է այս ընկերության համար։ Եթե ոչ մեկը ընտրված չէ, մատակարարը հասանելի է ամեն օր։"),
                ["Admin.Companies.Company.Vendors.Schedule.MarkDayOff"] =
                    ("Mark day off",
                     "Նշել որպես ոչ աշխատանքային օր"),
                ["Admin.Companies.Company.Vendors.Schedule.RestoreDay"] =
                    ("Restore day",
                     "Վերականգնել օրը"),
                ["Admin.Companies.Company.Vendors.Schedule.Saved"] =
                    ("The schedule has been saved successfully.",
                     "Ժամանակացույցը հաջողությամբ պահպանվեց։"),
                ["Order.VendorNotAvailableOnScheduledDate"] =
                    ("This vendor is not available for delivery on the selected date.",
                     "Այս մատակարարը հասանելի չէ ընտրված օրով առաքման համար։")
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
