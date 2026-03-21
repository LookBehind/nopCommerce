using FluentMigrator;
using Nop.Core.Domain.Companies;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.UpgradeTo440
{
    [NopMigration("2022/04/30 10:30:17:6453226", "CompanyVendor")]
    [SkipMigrationOnInstall]
    public class CompanyVendorMappingMigration : MigrationBase
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor
        public CompanyVendorMappingMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }
        #endregion

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CompanyVendor))).Exists())
                _migrationManager.BuildTable<CompanyVendor>(Create);
        }
    }
}