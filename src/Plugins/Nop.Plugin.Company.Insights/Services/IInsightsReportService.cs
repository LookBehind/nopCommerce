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
        /// <summary>The catalog of available reports (metadata incl. tunable parameters).</summary>
        IList<InsightsReportMeta> GetCatalog();

        /// <summary>
        /// Run a report by id with optional parameters (e.g. {"days":"60"}), clamped to each
        /// parameter's declared [Min, Max]. Null/missing values use the declared defaults.
        /// Returns null if the id is unknown.
        /// </summary>
        Task<InsightsReportResult> RunAsync(string id, IDictionary<string, string> parameters);

        /// <summary>
        /// Structured order aggregation for the agent (no free SQL). groupBy: "day" | "status";
        /// metric: "count" | "revenue". <paramref name="days"/> is the look-back window (clamped).
        /// </summary>
        Task<InsightsReportResult> QueryOrdersAsync(int days, string groupBy, string metric);

        /// <summary>
        /// Product reviews (joined to product for vendor + customer for email/name) created within the
        /// last <paramref name="days"/> (clamped to 90). Optional vendor / customer id / customer email
        /// filters; orderBy: "date" | "rating" | "helpful". <paramref name="limit"/> row cap
        /// (default 50, clamped to 200). Includes triage columns (who/when/resolution + triage hours).
        /// </summary>
        Task<InsightsReportResult> GetReviewsAsync(int days, int? vendorId, int? customerId, string customerEmail, string orderBy, int? limit);
    }
}
