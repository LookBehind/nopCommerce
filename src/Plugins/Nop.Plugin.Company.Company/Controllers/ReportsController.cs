using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.Company.Models.Reporting;
using Nop.Plugin.Company.Company.Services.Reporting;
using Nop.Services.Customers;
using Nop.Web.Controllers;
using OfficeOpenXml;
using OfficeOpenXml.Style;

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
            var (ok, redirect, isAdmin) = await AuthorizeAsync();
            if (!ok)
                return redirect;

            var (f, t) = await NormalizeAsync(from, to);

            var model = new ReportsDashboardModel { From = f, To = t, IsAdmin = isAdmin };
            foreach (var def in reportService.ListWidgets(isAdmin))
                model.Widgets.Add(await reportService.RunWidgetAsync(def.Id, f, t, isAdmin));

            return View("~/Plugins/Company.Company/Views/Reports/Index.cshtml", model);
        }

        public async Task<IActionResult> Export(DateTime? from, DateTime? to)
        {
            var (ok, redirect, isAdmin) = await AuthorizeAsync();
            if (!ok)
                return redirect;

            var (f, t) = await NormalizeAsync(from, to);

            using var package = new ExcelPackage();
            var used = new HashSet<string>();

            foreach (var def in reportService.ListWidgets(isAdmin))
            {
                var r = await reportService.RunWidgetAsync(def.Id, f, t, isAdmin);
                var ws = package.Workbook.Worksheets.Add(UniqueSheetName(r.Title, used));

                for (var c = 0; c < r.Columns.Count; c++)
                    ws.Cells[1, c + 1].Value = r.Columns[c];

                for (var i = 0; i < r.Rows.Count; i++)
                    for (var c = 0; c < r.Columns.Count; c++)
                        ws.Cells[i + 2, c + 1].Value = r.Rows[i][c];

                if (r.Columns.Count > 0)
                {
                    using var header = ws.Cells[1, 1, 1, r.Columns.Count];
                    header.Style.Font.Bold = true;
                    header.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    // Fixed widths instead of AutoFitColumns — the latter measures text via
                    // GDI (System.Drawing) and throws on Linux without libgdiplus.
                    for (var c = 1; c <= r.Columns.Count; c++)
                        ws.Column(c).Width = 22;
                    ws.View.FreezePanes(2, 1);
                }
            }

            var bytes = package.GetAsByteArray();
            var fileName = $"reports_{f:yyyyMMdd}_{t:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        #region Helpers

        // nopCommerce storefront uses cookie auth, but the default scheme is JWT
        // (mobile API), so [Authorize] would reject cookie sessions. Gate manually.
        private async Task<(bool ok, IActionResult redirect, bool isAdmin)> AuthorizeAsync()
        {
            var customer = await workContext.GetCurrentCustomerAsync();

            if (!await customerService.IsRegisteredAsync(customer))
                return (false, RedirectToRoute("Login", new { returnUrl = Url.RouteUrl("Plugin.Company.Company.Reports") }), false);

            var isAdmin = await customerService.IsInCustomerRoleAsync(customer, NopCustomerDefaults.AdministratorsRoleName);
            var canView = isAdmin || await customerService.IsInCustomerRoleAsync(
                customer, CompanyReportingDefaults.CompanyDashboardViewerRoleSystemName);

            if (!canView)
                return (false, RedirectToRoute("Homepage"), false);

            return (true, null, isAdmin);
        }

        // Default window: this week so far — Monday → today, in the tenant's local time
        // (offset from the customer's Company.TimeZone, not hardcoded).
        private async Task<(DateTime from, DateTime to)> NormalizeAsync(DateTime? from, DateTime? to)
        {
            var tz = await reportService.GetUtcOffsetMinutesAsync();
            var localToday = DateTime.UtcNow.AddMinutes(tz).Date;
            var daysSinceMonday = ((int)localToday.DayOfWeek + 6) % 7; // Mon=0 … Sun=6
            return (from ?? localToday.AddDays(-daysSinceMonday), to ?? localToday);
        }

        private static string UniqueSheetName(string title, HashSet<string> used)
        {
            var name = Regex.Replace(title ?? "Sheet", @"[:\\/?*\[\]]", " ").Trim();
            if (name.Length > 31)
                name = name[..31];
            if (string.IsNullOrWhiteSpace(name))
                name = "Sheet";

            var baseName = name;
            var n = 1;
            while (!used.Add(name))
            {
                var suffix = $" ({++n})";
                name = (baseName.Length + suffix.Length > 31 ? baseName[..(31 - suffix.Length)] : baseName) + suffix;
            }
            return name;
        }

        #endregion
    }
}
