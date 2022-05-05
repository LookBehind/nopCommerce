using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Vendors;
using Nop.Services.Companies;
using Nop.Web.Factories;
using Nop.Web.Framework.Components;

namespace Nop.Web.Components
{
    public class VendorNavigationViewComponent : NopViewComponent
    {
        private readonly ICatalogModelFactory _catalogModelFactory;
        private readonly VendorSettings _vendorSettings;
        private readonly IWorkContext _workContext;
        private readonly ICompanyService _companyService;

        public VendorNavigationViewComponent(ICatalogModelFactory catalogModelFactory,
            VendorSettings vendorSettings, IWorkContext workContext, ICompanyService companyService)
        {
            _catalogModelFactory = catalogModelFactory;
            _vendorSettings = vendorSettings;
            _workContext = workContext;
            _companyService = companyService;
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (_vendorSettings.VendorsBlockItemsToDisplay == 0)
                return Content("");
            var customer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
            var vendors = (await _companyService.GetCompanyVendorsByCompanyAsync(company == null ? 0 : company.Id))
                .Select(v => v.VendorId).ToArray();
            var model = await _catalogModelFactory.PrepareVendorNavigationModelAsync(vendors);
            if (!model.Vendors.Any())
                return Content("");

            return View(model);
        }
    }
}
