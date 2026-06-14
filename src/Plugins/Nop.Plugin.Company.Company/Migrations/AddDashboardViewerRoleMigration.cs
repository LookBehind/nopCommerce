using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Plugin.Company.Company.Services.Reporting;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Adds the "Company Dashboard Viewer" customer role used to gate the company-wide
    /// reporting dashboard (storefront page + /api/reports). Idempotent; runs on both
    /// install and upgrade (InstallAsync does not seed it, so this is NOT skip-on-install).
    /// </summary>
    [NopMigration("2026/06/14 12:00:00:0000000", "Company.AddCompanyDashboardViewerRole")]
    public class AddDashboardViewerRoleMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public AddDashboardViewerRoleMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var exists = _dataProvider.GetTable<CustomerRole>()
                .Any(r => r.SystemName == CompanyReportingDefaults.CompanyDashboardViewerRoleSystemName);

            if (exists)
                return;

            _dataProvider.InsertEntityAsync(new CustomerRole
            {
                Name = CompanyReportingDefaults.CompanyDashboardViewerRoleName,
                SystemName = CompanyReportingDefaults.CompanyDashboardViewerRoleSystemName,
                Active = true,
                IsSystemRole = false,
                EnablePasswordLifetime = false
            }).GetAwaiter().GetResult();
        }

        public override void Down()
        {
            // No rollback — leave the role in place to avoid orphaning role mappings.
        }
    }
}
