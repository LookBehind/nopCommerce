using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Company.Services.Reporting
{
    /// <summary>
    /// The "widget" layer for company reporting. Backs both the storefront dashboard
    /// page and the /api/reports endpoints. Each query runs against the current
    /// tenant's own DB (the app's connection) — isolation is structural.
    /// </summary>
    public interface IReportService
    {
        /// <summary>Widgets available to the caller (admin sees admin-only ones too).</summary>
        IList<WidgetDefinition> ListWidgets(bool includeAdmin);

        /// <summary>UTC offset (minutes) for the current customer's company timezone.</summary>
        Task<int> GetUtcOffsetMinutesAsync();

        /// <summary>Run a widget by id. from/to are LOCAL (UTC+4) dates for DateRange widgets.</summary>
        Task<ReportResult> RunWidgetAsync(string widgetId, DateTime? from, DateTime? to, bool isAdmin);
    }
}
