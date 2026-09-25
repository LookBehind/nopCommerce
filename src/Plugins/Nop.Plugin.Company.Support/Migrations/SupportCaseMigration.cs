using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Company.Support.Domain;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Creates the tables backing customer support case inquiries and their status history.
    /// </summary>
    [NopMigration("2026/09/25 12:00:00:0000000", "Company.Support.Tables")]
    public class SupportCaseMigration : Migration
    {
        private readonly IMigrationManager _migrationManager;

        public SupportCaseMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(SupportCase))).Exists())
                _migrationManager.BuildTable<SupportCase>(Create);

            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(SupportCaseStatusHistory))).Exists())
                _migrationManager.BuildTable<SupportCaseStatusHistory>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
