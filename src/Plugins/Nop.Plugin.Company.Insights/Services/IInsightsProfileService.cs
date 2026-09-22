using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>A company the caller may scope to (admin company picker).</summary>
    public class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>Result of resolving the current user's profiles.</summary>
    public class ResolvedProfiles
    {
        public IList<InsightsProfile> Allowed { get; set; } = new List<InsightsProfile>();
        public string DefaultId { get; set; }
        public bool IsAdmin { get; set; }
        /// <summary>The user's own company id (from Company_Customer_Mapping), if any.</summary>
        public int? OwnCompanyId { get; set; }
        /// <summary>Companies the user may pick from when a scoped profile needs an explicit choice (admins).</summary>
        public IList<CompanyOption> SelectableCompanies { get; set; } = new List<CompanyOption>();

        /// <summary>True when the default profile is company-scoped but the user has no company and cannot
        /// pick one (a non-admin Workplace Manager not linked to any company). Every query then fails closed,
        /// so the UI should tell them to get linked instead of showing empty data.</summary>
        public bool CompanyLinkRequired { get; set; }
    }

    /// <summary>
    /// Resolves which profiles a signed-in backoffice user may use (from their CustomerRoles) and the
    /// authoritative data scope for a chosen profile. Enforced server-side; the client picker is only
    /// a convenience. See docs/USER-PROFILES.md.
    /// </summary>
    public interface IInsightsProfileService
    {
        IList<InsightsProfile> GetAllProfiles();

        /// <summary>Profiles the current user may use + default + admin/company context.</summary>
        Task<ResolvedProfiles> ResolveForCurrentUserAsync(CancellationToken cancellationToken = default);

        /// <summary>The allowed profile matching <paramref name="profileId"/>, or the user's default. Never returns a disallowed profile.</summary>
        Task<InsightsProfile> GetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Authoritative data scope for the active profile. For a company-scoped profile the company is
        /// the user's own mapping (client input ignored); an admin acting scoped must pass a valid
        /// <paramref name="requestedCompanyId"/> or the scope is denied (fail-closed).
        /// </summary>
        Task<ReportScope> ResolveScopeAsync(InsightsProfile profile, int? requestedCompanyId, CancellationToken cancellationToken = default);

        /// <summary>Data scope for a company by id (no current user needed) — used by background automations.
        /// Null companyId = unscoped (all companies); otherwise scoped to that company + its vendors.</summary>
        Task<ReportScope> ScopeForCompanyAsync(int? companyId, CancellationToken cancellationToken = default);
    }
}
