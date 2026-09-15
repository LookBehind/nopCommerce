using System.Collections.Generic;

namespace Nop.Plugin.Company.Insights.Models
{
    /// <summary>
    /// A profile ("view") = the primary interactive context. Tied to nopCommerce CustomerRoles,
    /// it drives the assistant persona, the allowed report/tool set, and the data scope.
    /// See docs/USER-PROFILES.md.
    /// </summary>
    public class InsightsProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>CustomerRole SystemNames that grant this profile.</summary>
        public string[] RoleSystemNames { get; set; } = new string[0];

        /// <summary>Assistant system-prompt focus for this profile.</summary>
        public string Persona { get; set; }

        /// <summary>Catalog report ids allowed under this profile (null = all).</summary>
        public string[] AllowedReports { get; set; }

        /// <summary>True = data is restricted to a single company (Workplace Manager).</summary>
        public bool CompanyScoped { get; set; }

        public bool ReportAllowed(string reportId) =>
            AllowedReports == null || System.Array.IndexOf(AllowedReports, reportId) >= 0;
    }

    /// <summary>
    /// Server-resolved data scope applied to every report/tool query. Authoritative — never built
    /// from client input for a company-scoped profile.
    /// </summary>
    public class ReportScope
    {
        /// <summary>Fail-closed: the query must return nothing (e.g. Workplace Manager with no company).</summary>
        public bool Denied { get; set; }

        /// <summary>When set, restrict orders to this company. Null (and not Denied) = unscoped (all companies).</summary>
        public int? CompanyId { get; set; }

        /// <summary>The company's vendor ids (for catalog/vendor reports). Null = unscoped.</summary>
        public IList<int> VendorIds { get; set; }

        public static ReportScope Unscoped() => new ReportScope();
        public static ReportScope DeniedScope() => new ReportScope { Denied = true };
    }
}
