using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Resolves the owning company for catalog entities so events that aren't inherently company-scoped
    /// (product/review) can still be matched to a Workplace Manager's company-scoped automations. A product
    /// belongs to a vendor, and a vendor maps to a company (Company_Vendor_Mapping).
    /// </summary>
    public interface IInsightsCompanyResolver
    {
        /// <summary>The company that owns the vendor (null if none / unmapped). First match if several.</summary>
        Task<int?> CompanyForVendorAsync(int vendorId, CancellationToken cancellationToken = default);

        /// <summary>The vendor of a product (0 if none).</summary>
        Task<int> VendorForProductAsync(int productId, CancellationToken cancellationToken = default);
    }
}
