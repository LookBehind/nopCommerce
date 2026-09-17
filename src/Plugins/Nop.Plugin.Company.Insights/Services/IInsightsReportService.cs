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
        /// <paramref name="scope"/> is the authoritative company scope (null = unscoped).
        /// Returns null if the id is unknown.
        /// </summary>
        Task<InsightsReportResult> RunAsync(string id, IDictionary<string, string> parameters, ReportScope scope = null);

        /// <summary>
        /// Structured order aggregation for the agent (no free SQL). groupBy: "day" | "status";
        /// metric: "count" | "revenue". <paramref name="days"/> is the look-back window (clamped).
        /// </summary>
        Task<InsightsReportResult> QueryOrdersAsync(int days, string groupBy, string metric, ReportScope scope = null);

        /// <summary>
        /// Product reviews (joined to product for vendor + customer for email/name) created within the
        /// last <paramref name="days"/> (clamped to 90). Optional vendor / customer email / customer
        /// name filters; orderBy: "date" | "rating" | "helpful". <paramref name="limit"/> row cap
        /// (default 50, clamped to 200). Customer is shown as full name + email (never CustomerId).
        /// Includes triage columns (who/when/resolution + triage hours).
        /// </summary>
        Task<InsightsReportResult> GetReviewsAsync(int days, int? vendorId, string customerEmail, string customerName, string orderBy, int? limit, ReportScope scope = null);

        /// <summary>Product lookup (agent tool): filter by id/vendor/name/category, orderBy name|price|created|id,
        /// with short/full description, weight, SKU, price, categories and picture URLs. Company-scoped.</summary>
        Task<InsightsReportResult> ListProductsAsync(int? id, int? vendorId, string name, string category, string orderBy, int? limit, ReportScope scope = null);

        /// <summary>Order lookup (agent tool): by delivery-date range + status, and optional filters — customer
        /// email / full name, orders containing a vendor's products (vendorId) or a specific product (productId).
        /// Returns customer name/email + item summary. Company-scoped via Order.CompanyId.</summary>
        Task<InsightsReportResult> ListOrdersAsync(string from, string to, string status, int? limit, ReportScope scope = null,
            string customerEmail = null, string customerName = null, int? vendorId = null, int? productId = null);
    }
}
