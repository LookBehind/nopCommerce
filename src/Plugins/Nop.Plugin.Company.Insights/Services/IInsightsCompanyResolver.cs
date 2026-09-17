using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Resolves the owning company/companies for catalog entities so events that aren't inherently
    /// company-scoped (product/review) can still be matched to a Workplace Manager's company-scoped
    /// automations. A product belongs to a vendor, and a vendor maps to one or more companies
    /// (Company_Vendor_Mapping); an update fans out to all of them.
    /// </summary>
    public interface IInsightsCompanyResolver
    {
        /// <summary>Every company that owns the vendor (empty if none / unmapped).</summary>
        Task<IList<int>> CompaniesForVendorAsync(int vendorId, CancellationToken cancellationToken = default);

        /// <summary>The vendor of a product (0 if none).</summary>
        Task<int> VendorForProductAsync(int productId, CancellationToken cancellationToken = default);
    }
}
