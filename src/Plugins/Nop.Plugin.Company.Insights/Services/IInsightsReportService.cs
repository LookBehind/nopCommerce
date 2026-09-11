using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Named, read-only reports the Insights workspace can render. Runs inside the tenant
    /// instance, so every query is naturally scoped to that tenant's database.
    /// </summary>
    public interface IInsightsReportService
    {
        /// <summary>The catalog of available reports (metadata only).</summary>
        IList<InsightsReportMeta> GetCatalog();

        /// <summary>Run a report by id; returns null if the id is unknown.</summary>
        Task<InsightsReportResult> RunAsync(string id, DateTime? fromUtc, DateTime? toUtc);

        /// <summary>
        /// Structured order aggregation for the agent (no free SQL). groupBy: "day" | "status";
        /// metric: "count" | "revenue".
        /// </summary>
        Task<InsightsReportResult> QueryOrdersAsync(DateTime? fromUtc, DateTime? toUtc, string groupBy, string metric);
    }
}
