using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Company.Support.Domain;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Creates the table backing a support case's reply thread (SupportCaseMessage).
    /// </summary>
    [NopMigration("2026/09/26 09:00:00:0000000", "Company.Support.Messages")]
    public class SupportCaseMessageMigration : Migration
    {
        private readonly IMigrationManager _migrationManager;

        public SupportCaseMessageMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(SupportCaseMessage))).Exists())
                _migrationManager.BuildTable<SupportCaseMessage>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
