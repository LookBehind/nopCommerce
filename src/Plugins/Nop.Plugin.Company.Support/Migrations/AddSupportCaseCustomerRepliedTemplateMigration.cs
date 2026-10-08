using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Seeds the "customer replied" store-owner notification message template. Idempotent -
    /// only inserts if a template with this system name doesn't already exist. See
    /// AddSupportCaseStaffNotificationTemplateMigration for the sibling "new case" template.
    /// </summary>
    [NopMigration("2026/10/08 12:05:00:0000000", "Company.Support.AddCustomerRepliedTemplate")]
    public class AddSupportCaseCustomerRepliedTemplateMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddSupportCaseCustomerRepliedTemplateMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var alreadyExists = _dataProvider.GetTable<MessageTemplate>()
                .Any(mt => mt.Name == SupportMessageTemplateSystemNames.SupportCaseCustomerRepliedStoreOwnerNotification);

            if (alreadyExists)
                return;

            var emailAccount = _dataProvider.GetTable<EmailAccount>().FirstOrDefault();
            if (emailAccount == null)
                return;

            var template = new MessageTemplate
            {
                Name = SupportMessageTemplateSystemNames.SupportCaseCustomerRepliedStoreOwnerNotification,
                Subject = "%Store.Name%. Customer replied on support case #%SupportCase.Id%",
                Body = $"<p>{Environment.NewLine}<a href=\"%Store.URL%\">%Store.Name%</a>{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}" +
                    $"%Customer.FullName% (%Customer.Email%) replied on support case #%SupportCase.Id%.{Environment.NewLine}<br />{Environment.NewLine}" +
                    $"%SupportCase.NewMessageText%{Environment.NewLine}<br />{Environment.NewLine}<br />{Environment.NewLine}" +
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
