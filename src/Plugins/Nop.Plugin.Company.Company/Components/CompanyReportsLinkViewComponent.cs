using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.Company.Services.Reporting;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Company.Company.Components
{
    /// <summary>
    /// Renders a "Reports" header link, only for customers in the CompanyDashboardViewer
    /// role (or admins). Injected into the HeaderLinksAfter widget zone.
    /// </summary>
    [ViewComponent(Name = "CompanyReportsLink")]
    public class CompanyReportsLinkViewComponent(
        IWorkContext workContext,
        ICustomerService customerService)
        : NopViewComponent
    {
        public async Task<IViewComponentResult> InvokeAsync()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            if (await customerService.IsGuestAsync(customer))
                return Content(string.Empty);

            var canView = await customerService.IsInCustomerRoleAsync(customer, NopCustomerDefaults.AdministratorsRoleName)
                || await customerService.IsInCustomerRoleAsync(customer, CompanyReportingDefaults.CompanyDashboardViewerRoleSystemName);

            if (!canView)
                return Content(string.Empty);

            return View("~/Plugins/Company.Company/Views/Shared/Components/CompanyReportsLink/Default.cshtml");
        }
    }
}
