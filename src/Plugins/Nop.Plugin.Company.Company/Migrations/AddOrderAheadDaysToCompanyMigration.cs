using FluentMigrator;
using LinqToDB.Reflection;
using Nop.Core.Domain.Companies;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    [NopMigration("2025/09/15 12:00:00:0000000", "Company.AddOrderAheadDays")]
    public class AddOrderAheadDaysToCompanyMigration : Migration
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public AddOrderAheadDaysToCompanyMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        #endregion
        
        #region Methods

        /// <summary>
        /// Collect the UP migration expressions
        /// </summary>
        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(Core.Domain.Companies.Company)))
                .Column(nameof(Core.Domain.Companies.Company.OrderAheadDays)).Exists())
            {
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Core.Domain.Companies.Company)))
                    .AddColumn(nameof(Core.Domain.Companies.Company.OrderAheadDays))
                    .AsInt32()
                    .NotNullable()
                    .WithDefaultValue(14);
            }
        }

        /// <summary>
        /// Collect the DOWN migration expressions
        /// </summary>
        public override void Down()
        {
            if (Schema.Table(NameCompatibilityManager.GetTableName(typeof(Core.Domain.Companies.Company)))
                .Column(nameof(Core.Domain.Companies.Company.OrderAheadDays)).Exists())
            {
                Delete.Column(nameof(Core.Domain.Companies.Company.OrderAheadDays))
                    .FromTable(NameCompatibilityManager.GetTableName(typeof(Core.Domain.Companies.Company)));
            }
        }

        #endregion
    }
}
