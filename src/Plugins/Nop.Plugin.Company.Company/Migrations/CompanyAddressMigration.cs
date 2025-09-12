using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Company.Company.Domain;

namespace Nop.Plugin.Company.Company.Data
{
    [NopMigration("2024/01/15 10:00:00:0000000", "Company Address Migration")]
    public class CompanyAddressMigration : MigrationBase
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public CompanyAddressMigration(IMigrationManager migrationManager)
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
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CompanyAddress))).Exists())
                _migrationManager.BuildTable<CompanyAddress>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }

        #endregion
    }
}
