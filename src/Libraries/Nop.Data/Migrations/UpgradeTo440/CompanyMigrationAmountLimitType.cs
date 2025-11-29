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
                !companyTable.Column(nameof(Company.AmountLimitTypeId)).Exists())
            {
                Alter
                    .Table(NameCompatibilityManager.GetTableName(typeof(Company)))
                    .AddColumn(nameof(Company.AmountLimitTypeId))
                    .AsInt32()
                    .WithDefaultValue((int)AmountLimitType.Daily);
            }
        }

        public override void Down()
        {
            var companyTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Company)));
            if (companyTable.Exists() &&
                companyTable.Column(nameof(Company.AmountLimitTypeId)).Exists())
            {
                Delete
                    .Column(nameof(Company.AmountLimitTypeId))
                    .FromTable(NameCompatibilityManager.GetTableName(typeof(Company)));
            }
        }

        #endregion
    }
}
