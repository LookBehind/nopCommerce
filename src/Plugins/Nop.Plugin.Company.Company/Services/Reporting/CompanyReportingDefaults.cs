namespace Nop.Plugin.Company.Company.Services.Reporting
{
    /// <summary>
    /// Shared constants for the company reporting feature (dashboard page + /api/reports).
    /// </summary>
    public static class CompanyReportingDefaults
    {
        /// <summary>
        /// Customer role gating the COMPANY-wide reporting dashboard (all of a
        /// company's orders/vendors). Distinct from any future "personal reports"
        /// access that would let an individual customer see only their own data
        /// (ordering habits, top categories, etc.).
        /// </summary>
        public const string CompanyDashboardViewerRoleSystemName = "CompanyDashboardViewer";

        public const string CompanyDashboardViewerRoleName = "Company Dashboard Viewer";
    }
}
