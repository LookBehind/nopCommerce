using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Seeds the "new support case" store-owner notification message template. Idempotent -
    /// only inserts if a template with this system name doesn't already exist, safe on
    /// install and upgrade alike. Uses the first configured email account, same fallback
    /// InstallationService uses for core templates.
    /// </summary>
    [NopMigration("2026/10/08 12:00:00:0000000", "Company.Support.AddNewCaseStaffNotificationTemplate")]
    public class AddSupportCaseStaffNotificationTemplateMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddSupportCaseStaffNotificationTemplateMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var alreadyExists = _dataProvider.GetTable<MessageTemplate>()
                .Any(mt => mt.Name == SupportMessageTemplateSystemNames.NewSupportCaseStoreOwnerNotification);

            if (alreadyExists)
                return;

            var emailAccount = _dataProvider.GetTable<EmailAccount>().FirstOrDefault();
            if (emailAccount == null)
                return;

            var template = new MessageTemplate
            {
                Name = SupportMessageTemplateSystemNames.NewSupportCaseStoreOwnerNotification,
                Subject = "%Store.Name%. New support case submitted (#%SupportCase.Id%)",
                Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}" +
                    $"A new support case was submitted by %Customer.FullName% (%Customer.Email%).{Environment.NewLine}<br />{Environment.NewLine}" +
                    $"Category: %SupportCase.Category%{Environment.NewLine}<br />{Environment.NewLine}" +
                    $"%SupportCase.Description%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}" +
                    $"<a href=\"%SupportCase.SelfUrl%\">Open this case</a>{Environment.NewLine}</p>{Environment.NewLine}",
                IsActive = true,
                EmailAccountId = emailAccount.Id
            };

            _dataProvider.InsertEntityAsync(template).GetAwaiter().GetResult();
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
