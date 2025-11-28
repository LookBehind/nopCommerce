using FluentMigrator;
using LinqToDB.Reflection;
using Nop.Core.Domain.Catalog;
using Nop.Data.Mapping;
using Nop.Data.Extensions;
using Nop.Core.Domain.Companies;

namespace Nop.Data.Migrations.UpgradeTo440
{
    [NopMigration("2025/11/28 23:45:08:9037989", "CompanyMigrationAmountLimitType")]
    [SkipMigrationOnInstall]
    public class CompanyMigrationAmountLimitType : Migration
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public CompanyMigrationAmountLimitType(IMigrationManager migrationManager)
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
            var companyTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Company)));
            if (companyTable.Exists() &&
                !companyTable.Column(nameof(Company.AmountLimitType)).Exists())
            {
                Alter
                    .Table(NameCompatibilityManager.GetTableName(typeof(Company)))
                    .AddColumn(nameof(Company.AmountLimitType)).AsInt32()
                    .WithDefaultValue(AmountLimitType.Daily);
            }
        }

        public override void Down()
        {
            var companyTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Company)));
            if (companyTable.Exists() &&
                companyTable.Column(nameof(Company.AmountLimitType)).Exists())
            {
                Delete
                    .Column(nameof(Company.AmountLimitType))
                    .FromTable(NameCompatibilityManager.GetTableName(typeof(Company)));
            }
        }

        #endregion
    }
}
