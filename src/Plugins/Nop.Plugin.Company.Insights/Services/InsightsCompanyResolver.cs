using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Companies;

namespace Nop.Plugin.Company.Insights.Services
{
    public class InsightsCompanyResolver : IInsightsCompanyResolver
    {
        private readonly ICompanyService _companyService;
        private readonly INopDataProvider _dataProvider;

        public InsightsCompanyResolver(ICompanyService companyService, INopDataProvider dataProvider)
        {
            _companyService = companyService;
            _dataProvider = dataProvider;
        }

        public async Task<IList<int>> CompaniesForVendorAsync(int vendorId, CancellationToken cancellationToken = default)
        {
            if (vendorId <= 0)
                return new List<int>();
            var maps = await _companyService.GetCompanyVendorsByVendorIdAsync(vendorId);
            return maps?.Select(m => m.CompanyId).Where(id => id > 0).Distinct().ToList() ?? new List<int>();
        }

        public async Task<int> VendorForProductAsync(int productId, CancellationToken cancellationToken = default)
        {
            if (productId <= 0)
                return 0;
            return await _dataProvider.GetTable<Product>()
                .Where(p => p.Id == productId)
                .Select(p => p.VendorId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
