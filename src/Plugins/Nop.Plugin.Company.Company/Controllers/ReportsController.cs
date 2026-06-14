using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.Company.Models.Reporting;
using Nop.Plugin.Company.Company.Services.Reporting;
using Nop.Services.Customers;
using Nop.Web.Controllers;

namespace Nop.Plugin.Company.Company.Controllers
{
    /// <summary>
    /// Storefront company-reporting dashboard. Gated by the CompanyDashboardViewer role
    /// (admins see admin-only widgets too). Per-tenant isolation is structural — the
    /// service only ever reads this instance's own DB.
    /// </summary>
    public class ReportsController(
        IWorkContext workContext,
        ICustomerService customerService,
        IReportService reportService)
        : BasePublicController
    {
        public async Task<IActionResult> Index(DateTime? from, DateTime? to)
        {
            var customer = await workContext.GetCurrentCustomerAsync();

            // nopCommerce storefront auth is cookie-based, but the default auth scheme
            // is JWT (mobile API) — so [Authorize] would reject cookie sessions. Gate
            // manually like the built-in CustomerController does.
            if (!await customerService.IsRegisteredAsync(customer))
                return RedirectToRoute("Login", new { returnUrl = Url.RouteUrl("Plugin.Company.Company.Reports") });

            var isAdmin = await customerService.IsInCustomerRoleAsync(customer, NopCustomerDefaults.AdministratorsRoleName);
            var canView = isAdmin || await customerService.IsInCustomerRoleAsync(
                customer, CompanyReportingDefaults.CompanyDashboardViewerRoleSystemName);

            if (!canView)
                return RedirectToRoute("Homepage");

            // Default window: last 7 local (UTC+4) days.
            var localToday = DateTime.UtcNow.AddHours(4).Date;
            to ??= localToday;
            from ??= localToday.AddDays(-7);

            var model = new ReportsDashboardModel
            {
                From = from.Value,
                To = to.Value,
                IsAdmin = isAdmin
            };

            foreach (var def in reportService.ListWidgets(isAdmin))
                model.Widgets.Add(await reportService.RunWidgetAsync(def.Id, from, to, isAdmin));

            return View("~/Plugins/Company.Company/Views/Reports/Index.cshtml", model);
        }
    }
}
