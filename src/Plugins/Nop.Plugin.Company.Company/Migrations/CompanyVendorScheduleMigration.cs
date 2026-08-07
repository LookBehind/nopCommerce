using FluentMigrator;
using Nop.Core.Domain.Companies;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Creates the tables backing the per-company-vendor weekly working-day schedule
    /// and per-date day-off overrides.
    /// </summary>
    [NopMigration("2026/08/02 10:00:00:0000000", "Company.VendorSchedule")]
    public class CompanyVendorScheduleMigration : Migration
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public CompanyVendorScheduleMigration(IMigrationManager migrationManager)
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
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CompanyVendorWorkingDay))).Exists())
                _migrationManager.BuildTable<CompanyVendorWorkingDay>(Create);

            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CompanyVendorDayOff))).Exists())
                _migrationManager.BuildTable<CompanyVendorDayOff>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }

        #endregion
    }
}
