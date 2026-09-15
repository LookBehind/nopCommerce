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
    }
}
