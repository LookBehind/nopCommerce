using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Seeds the locale string resources used by the admin Support Cases list/edit pages.
    /// English defaults plus Armenian translations. Idempotent - only inserts keys not
    /// already present for a given language, safe on install and upgrade alike.
    /// </summary>
    [NopMigration("2026/09/25 12:05:00:0000000", "Company.Support.AddLocales")]
    public class AddSupportLocalesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddSupportLocalesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var resources = new Dictionary<string, (string En, string Hy)>
            {
                ["Admin.Support.Cases"] = ("Support Cases", "Աջակցության դիմումներ"),
                ["Admin.Support.Cases.Fields.Id"] = ("#", "#"),
                ["Admin.Support.Cases.Fields.Customer"] = ("Customer", "Հաճախորդ"),
                ["Admin.Support.Cases.Fields.Category"] = ("Category", "Կատեգորիա"),
                ["Admin.Support.Cases.Fields.Vendor"] = ("Vendor", "Մատակարար"),
                ["Admin.Support.Cases.Fields.Status"] = ("Status", "Կարգավիճակ"),
                ["Admin.Support.Cases.Fields.AssignedTo"] = ("Assigned to", "Հանձնարարված է"),
                ["Admin.Support.Cases.Fields.CreatedOn"] = ("Created on", "Ստեղծվել է"),
                ["Admin.Support.Cases.Fields.Subject"] = ("Subject", "Վերնագիր"),
                ["Admin.Support.Cases.Fields.Description"] = ("Description", "Նկարագրություն"),
                ["Admin.Support.Cases.List.SearchStatus"] = ("Status", "Կարգավիճակ"),
                ["Admin.Support.Cases.List.SearchCategory"] = ("Category", "Կատեգորիա"),
                ["Admin.Support.Cases.List.SearchUnassignedOnly"] = ("Unassigned only", "Միայն չհանձնարարվածները"),
                ["Admin.Support.Cases.AssignToMe"] = ("Assign to me", "Հանձնարարել ինձ"),
                ["Admin.Support.Cases.Assigned"] = ("This case has been assigned to you.", "Այս դիմումը հանձնարարվել է ձեզ։"),
                ["Admin.Support.Cases.StatusUpdated"] = ("Status updated successfully.", "Կարգավիճակը հաջողությամբ թարմացվեց։"),
                ["Admin.Support.Cases.NotAssigned"] = ("Unassigned", "Չհանձնարարված"),
                ["Admin.Support.Cases.StatusHistory"] = ("Status history", "Կարգավիճակի պատմություն"),
                ["Admin.Support.Cases.StatusHistory.Status"] = ("Status", "Կարգավիճակ"),
                ["Admin.Support.Cases.StatusHistory.EnteredOn"] = ("Entered on", "Մուտք է գործել"),
                ["Admin.Support.Cases.StatusHistory.Duration"] = ("Duration", "Տևողություն"),
                ["Admin.Support.Cases.Fields.Description.Hint"] = ("The full inquiry text as submitted by the customer.", "Հաճախորդի կողմից ուղարկված դիմումի ամբողջական տեքստը։")
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
