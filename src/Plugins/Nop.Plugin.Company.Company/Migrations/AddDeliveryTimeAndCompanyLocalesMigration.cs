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
    /// Seeds the locale string resources used by the Company plugin's storefront delivery-time
    /// picker (DeliveryTime.*) and admin Company screens that were referenced in code but never
    /// registered. Their absence produced thousands of "Resource string (...) is not found"
    /// warnings in the log of every tenant. Seeds English defaults plus Armenian translations
    /// (the tenants run English + Armenian, the Armenian language has UniqueSeoCode "am").
    /// Idempotent — only inserts keys not already present for a given language, so it is safe on
    /// install AND upgrade. Uses INopDataProvider raw inserts.
    /// </summary>
    [NopMigration("2026/06/17 10:00:00:0000000", "Company.AddDeliveryTimeAndCompanyLocales")]
    public class AddDeliveryTimeAndCompanyLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddDeliveryTimeAndCompanyLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // ResourceName -> (English, Armenian)
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                // Storefront / mobile delivery-time picker
                ["DeliveryTime.Retrieved"] =
                    ("Please select your delivery time.",
                     "Խնդրում ենք ընտրել առաքման ժամը։"),
                ["DeliveryTime.SelectionSaved"] =
                    ("Your delivery time has been saved.",
                     "Ձեր առաքման ժամը պահպանվեց։"),
                ["DeliveryTime.InvalidTime"] =
                    ("The selected delivery time is not available. Please choose another.",
                     "Ընտրված առաքման ժամը հասանելի չէ։ Խնդրում ենք ընտրել մեկ այլ ժամ։"),
                ["DeliveryTime.ErrorSaving"] =
                    ("Could not save your delivery time. Please try again.",
                     "Չհաջողվեց պահպանել առաքման ժամը։ Խնդրում ենք կրկին փորձել։"),
                ["DeliveryTime.ErrorRetrieving"] =
                    ("Could not load delivery time information. Please try again.",
                     "Չհաջողվեց բեռնել առաքման ժամի տվյալները։ Խնդրում ենք կրկին փորձել։"),
                ["DeliveryTime.SelectionCleared"] =
                    ("Your delivery time selection has been cleared.",
                     "Ձեր առաքման ժամի ընտրությունը մաքրվեց։"),
                ["DeliveryTime.ErrorClearing"] =
                    ("Could not clear your delivery time selection. Please try again.",
                     "Չհաջողվեց մաքրել առաքման ժամի ընտրությունը։ Խնդրում ենք կրկին փորձել։"),
                ["DeliveryTime.Prompt.NoSelection"] =
                    ("Please select a delivery time before placing your order.",
                     "Խնդրում ենք ընտրել առաքման ժամը նախքան պատվերը կատարելը։"),
                ["DeliveryTime.Prompt.SelectionInvalid"] =
                    ("Your selected delivery time is no longer available. Please choose another.",
                     "Ձեր ընտրած առաքման ժամն այլևս հասանելի չէ։ Խնդրում ենք ընտրել մեկ այլ ժամ։"),

                // Admin - Company notifications
                ["Admin.Company.Companies.Added"] =
                    ("The new company has been added successfully.",
                     "Նոր ընկերությունը հաջողությամբ ավելացվեց։"),
                ["Admin.Company.Companies.Updated"] =
                    ("The company has been updated successfully.",
                     "Ընկերությունը հաջողությամբ թարմացվեց։"),
                ["Admin.Company.Companies.Deleted"] =
                    ("The company has been deleted successfully.",
                     "Ընկերությունը հաջողությամբ ջնջվեց։"),
                ["Admin.Companies.Company.Addresses.Added"] =
                    ("The address has been added successfully.",
                     "Հասցեն հաջողությամբ ավելացվեց։"),
                ["Admin.Companies.Company.Addresses.Updated"] =
                    ("The address has been updated successfully.",
                     "Հասցեն հաջողությամբ թարմացվեց։"),

                // Admin - Company validation messages
                ["Admin.Company.Companies.Fields.Name.Required"] =
                    ("Please enter a name.",
                     "Խնդրում ենք մուտքագրել անվանումը։"),
                ["Admin.Company.Companies.Fields.Name.AmountLimit"] =
                    ("Please enter an amount limit.",
                     "Խնդրում ենք մուտքագրել գումարի սահմանաչափը։"),

                // Admin - Company field labels
                ["Admin.Companies.Company.Fields.AmountLimitType"] =
                    ("Amount limit type", "Գումարի սահմանաչափի տեսակ"),
                ["Admin.Companies.Company.Fields.OrderAheadDays"] =
                    ("Order ahead days", "Կանխատես պատվերի օրեր"),
                ["Admin.Customer.Customers.Fields.CustomerFullName"] =
                    ("Full name", "Ամբողջական անուն"),
                ["Admin.Customer.Customers.Products.Fields.Email"] =
                    ("Email", "Էլ. փոստ")
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
