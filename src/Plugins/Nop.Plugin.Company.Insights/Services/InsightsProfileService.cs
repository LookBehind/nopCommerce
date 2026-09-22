using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Companies;
using Nop.Services.Customers;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Profile registry + role→profile resolution + company-scope resolution. The registry is static
    /// (two profiles today); role SystemNames are constants but could be made env-tunable per tenant.
    /// </summary>
    public class InsightsProfileService : IInsightsProfileService
    {
        // Role SystemNames that grant each profile (see docs/USER-PROFILES.md §6).
        private const string RoleAdministrators = "Administrators";
        private const string RoleCompanyDashboardViewer = "CompanyDashboardViewer";

        public const string BackofficeId = "backoffice";
        public const string WorkplaceManagerId = "workplace-manager";

        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly ICompanyService _companyService;

        public InsightsProfileService(
            IWorkContext workContext,
            ICustomerService customerService,
            ICompanyService companyService)
        {
            _workContext = workContext;
            _customerService = customerService;
            _companyService = companyService;
        }

        private static readonly IList<Models.InsightsProfile> Registry = new List<Models.InsightsProfile>
        {
            new Models.InsightsProfile
            {
                Id = BackofficeId,
                Name = "Backoffice Support",
                Description = "MySnacks staff — vendors, deliveries and tenant requests across all companies.",
                RoleSystemNames = new[] { RoleAdministrators },
                CompanyScoped = false,
                AllowedReports = null, // all
                Persona = "an operations analyst for MySnacks backoffice support, focused on order flow, " +
                          "vendors and deliveries across the whole store"
            },
            new Models.InsightsProfile
            {
                Id = WorkplaceManagerId,
                Name = "Workplace Manager",
                Description = "The company's organizer — vendor traction, category health, reviews and delivery reliability for their own company.",
                // Admins get this too (dual view) — see docs §6.
                RoleSystemNames = new[] { RoleCompanyDashboardViewer, RoleAdministrators },
                CompanyScoped = true,
                AllowedReports = new[]
                {
                    "orders-per-day", "orders-by-status", "reviews",
                    "vendor-traction", "products-per-category", "products-per-category-per-vendor",
                    "vendor-delivery-reliability"
                },
                Persona = "a BI analyst for a company's workplace-food program on MySnacks, focused on vendor " +
                          "performance, catalog/category health, reviews and delivery reliability for that one company"
            }
        };

        public IList<Models.InsightsProfile> GetAllProfiles() => Registry;

        public async Task<ResolvedProfiles> ResolveForCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var roles = customer == null
                ? new List<string>()
                : (await _customerService.GetCustomerRolesAsync(customer)).Select(r => r.SystemName).ToList();

            var isAdmin = roles.Contains(RoleAdministrators);

            var allowed = Registry
                .Where(p => p.RoleSystemNames.Any(rn => roles.Contains(rn)))
                .ToList();

            var result = new ResolvedProfiles
            {
                Allowed = allowed,
                IsAdmin = isAdmin
            };

            // Own company (a Workplace Manager who is a company member).
            if (customer != null)
            {
                var own = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
                result.OwnCompanyId = own?.Id;
            }

            // Default: admins land on Backoffice; otherwise the first allowed profile.
            result.DefaultId = isAdmin
                ? BackofficeId
                : (allowed.FirstOrDefault()?.Id ?? BackofficeId);

            // A non-admin whose default profile is company-scoped but who has no company mapping (and no
            // company picker — that's admin-only) is stuck on a denied scope: flag it so the UI can explain.
            var defaultProfile = Registry.FirstOrDefault(p => p.Id == result.DefaultId);
            result.CompanyLinkRequired = !isAdmin
                && defaultProfile != null && defaultProfile.CompanyScoped
                && result.OwnCompanyId == null;

            // Admins acting as Workplace Manager must pick a company → provide the list.
            if (isAdmin && allowed.Any(p => p.Id == WorkplaceManagerId))
            {
                var companies = await _companyService.GetAllCompaniesAsync(pageIndex: 0, pageSize: 500);
                result.SelectableCompanies = companies
                    .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
                    .OrderBy(c => c.Name)
                    .ToList();
            }

            return result;
        }

        public async Task<Models.InsightsProfile> GetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            var resolved = await ResolveForCurrentUserAsync(cancellationToken);
            var match = resolved.Allowed.FirstOrDefault(p => p.Id == profileId);
            if (match != null)
                return match;
            return resolved.Allowed.FirstOrDefault(p => p.Id == resolved.DefaultId)
                   ?? resolved.Allowed.FirstOrDefault();
        }

        public async Task<Models.ReportScope> ResolveScopeAsync(Models.InsightsProfile profile, int? requestedCompanyId, CancellationToken cancellationToken = default)
        {
            if (profile == null)
                return Models.ReportScope.DeniedScope();

            // Unscoped profile (Backoffice): all companies, unless it optionally filters to one.
            if (!profile.CompanyScoped)
            {
                if (requestedCompanyId.HasValue && requestedCompanyId.Value > 0)
                {
                    var company = await _companyService.GetCompanyByIdAsync(requestedCompanyId.Value);
                    if (company == null)
                        return Models.ReportScope.Unscoped(); // ignore a bad optional filter
                    return await BuildScopeAsync(company.Id);
                }
                return Models.ReportScope.Unscoped();
            }

            // Company-scoped profile (Workplace Manager).
            var customer = await _workContext.GetCurrentCustomerAsync();
            var own = customer == null ? null : await _companyService.GetCompanyByCustomerIdAsync(customer.Id);

            // A real company member: their own company, ALWAYS (client input ignored).
            if (own != null)
                return await BuildScopeAsync(own.Id);

            // No own company (admin acting as Workplace Manager): require a valid explicit company.
            if (requestedCompanyId.HasValue && requestedCompanyId.Value > 0)
            {
                var company = await _companyService.GetCompanyByIdAsync(requestedCompanyId.Value);
                if (company != null)
                    return await BuildScopeAsync(company.Id);
            }

            // Fail closed.
            return Models.ReportScope.DeniedScope();
        }

        public async Task<Models.ReportScope> ScopeForCompanyAsync(int? companyId, CancellationToken cancellationToken = default)
        {
            if (companyId is not int cid || cid <= 0)
                return Models.ReportScope.Unscoped();
            return await BuildScopeAsync(cid);
        }

        private async Task<Models.ReportScope> BuildScopeAsync(int companyId)
        {
            var vendors = await _companyService.GetCompanyVendorsByCompanyAsync(companyId);
            return new Models.ReportScope
            {
                CompanyId = companyId,
                VendorIds = vendors.Select(v => v.VendorId).Distinct().ToList()
            };
        }
    }
}
