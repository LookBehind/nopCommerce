using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Data.Mapping;
using Nop.Data.Extensions;
using Nop.Core.Domain.Companies;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Data
{
    [NopMigration("2020/03/08 11:26:08:9037989", "Company")]
    [SkipMigrationOnInstall]
    public class CompanyMigration : MigrationBase
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public CompanyMigration(IMigrationManager migrationManager)
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
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(Core.Domain.Companies.Company))).Exists())
                _migrationManager.BuildTable<Core.Domain.Companies.Company>(Create);

            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CompanyCustomer))).Exists())
                _migrationManager.BuildTable<CompanyCustomer>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }

        #endregion
    }
}
