using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// P1 reports. Each pulls a bounded window of orders via LINQ2DB (provider-agnostic —
    /// works on SQL Server prod and PostgreSQL dev alike) and aggregates in memory, so there
    /// is no raw provider-specific SQL. Reports declare tunable parameters (a look-back window
    /// in days); callers may set them within the declared limits. Real Redash-audit reports get
    /// ported in later.
    /// </summary>
    public class InsightsReportService : IInsightsReportService
    {
        private const int QueryOrdersMaxDays = 365;

        public IList<InsightsReportMeta> GetCatalog()
        {
            return new List<InsightsReportMeta>
            {
                new InsightsReportMeta
                {
                    Id = "orders-per-day",
                    Name = "Orders per day",
                    Description = "Order count and revenue by day.",
                    DefaultChart = "line",
                    XField = "Date",
                    YField = "Orders",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "days", Label = "Look-back (days)", Default = 30, Min = 1, Max = 90 }
                    }
                },
                new InsightsReportMeta
                {
                    Id = "orders-by-status",
                    Name = "Orders by status",
                    Description = "Order count grouped by order status.",
                    DefaultChart = "pie",
                    CategoryField = "Status",
                    YField = "Orders",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "days", Label = "Look-back (days)", Default = 90, Min = 1, Max = 365 }
                    }
                },
                new InsightsReportMeta
                {
                    Id = "reviews",
                    Name = "Product reviews",
                    Description = "Recent product reviews (best viewed as a table). The agent can also filter by vendor/customer and reorder via list_reviews.",
                    DefaultChart = "bar",
                    XField = "Rating",
                    YField = "Rating",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "days", Label = "Look-back (days)", Default = 30, Min = 1, Max = 90 },
                        new InsightsReportParam { Name = "limit", Label = "Max rows", Default = 50, Min = 1, Max = 200 }
                    }
                }
            };
        }

        private readonly INopDataProvider _dataProvider;

        public InsightsReportService(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public async Task<InsightsReportResult> RunAsync(string id, IDictionary<string, string> parameters)
        {
            var meta = GetCatalog().FirstOrDefault(r => r.Id == id);
            if (meta == null)
                return null;

            var days = ResolveInt(meta, "days", parameters);

            return id switch
            {
                "orders-per-day" => await OrdersPerDayAsync(days),
                "orders-by-status" => await OrdersByStatusAsync(days),
                "reviews" => await GetReviewsAsync(days, null, null, null, "date", ResolveInt(meta, "limit", parameters)),
                _ => null
            };
        }

        public async Task<InsightsReportResult> QueryOrdersAsync(int days, string groupBy, string metric)
        {
            days = Math.Clamp(days <= 0 ? 30 : days, 1, QueryOrdersMaxDays);
            var orders = await LoadOrdersAsync(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow);

            var isRevenue = string.Equals(metric, "revenue", StringComparison.OrdinalIgnoreCase);
            var byStatus = string.Equals(groupBy, "status", StringComparison.OrdinalIgnoreCase);
            var metricName = isRevenue ? "Revenue" : "Orders";

            var result = new InsightsReportResult
            {
                Id = "query-orders",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = byStatus ? "Status" : "Date", Type = byStatus ? "string" : "date" },
                    new InsightsReportColumn { Name = metricName, Type = "number" }
                }
            };

            object Metric(IEnumerable<OrderSlim> g) =>
                isRevenue ? decimal.Round(g.Sum(x => x.OrderTotal), 2) : g.Count();

            if (byStatus)
            {
                foreach (var g in orders.GroupBy(o => o.OrderStatusId).OrderByDescending(g => g.Count()))
                    result.Rows.Add(new Dictionary<string, object>
                    {
                        ["Status"] = ((OrderStatus)g.Key).ToString(),
                        [metricName] = Metric(g)
                    });
            }
            else
            {
                foreach (var g in orders.GroupBy(o => o.CreatedOnUtc.Date).OrderBy(g => g.Key))
                    result.Rows.Add(new Dictionary<string, object>
                    {
                        ["Date"] = g.Key.ToString("yyyy-MM-dd"),
                        [metricName] = Metric(g)
                    });
            }

            return result;
        }

        private async Task<InsightsReportResult> OrdersPerDayAsync(int days)
        {
            var orders = await LoadOrdersAsync(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow);

            var result = new InsightsReportResult
            {
                Id = "orders-per-day",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Date", Type = "date" },
                    new InsightsReportColumn { Name = "Orders", Type = "number" },
                    new InsightsReportColumn { Name = "Revenue", Type = "number" }
                }
            };

            foreach (var g in orders.GroupBy(o => o.CreatedOnUtc.Date).OrderBy(g => g.Key))
            {
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Date"] = g.Key.ToString("yyyy-MM-dd"),
                    ["Orders"] = g.Count(),
                    ["Revenue"] = decimal.Round(g.Sum(x => x.OrderTotal), 2)
                });
            }

            return result;
        }

        private async Task<InsightsReportResult> OrdersByStatusAsync(int days)
        {
            var orders = await LoadOrdersAsync(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow);

            var result = new InsightsReportResult
            {
                Id = "orders-by-status",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Status", Type = "string" },
                    new InsightsReportColumn { Name = "Orders", Type = "number" }
                }
            };

            foreach (var g in orders.GroupBy(o => o.OrderStatusId).OrderByDescending(g => g.Count()))
            {
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Status"] = ((OrderStatus)g.Key).ToString(),
                    ["Orders"] = g.Count()
                });
            }

            return result;
        }

        public async Task<InsightsReportResult> GetReviewsAsync(int days, int? vendorId, int? customerId, string customerEmail, string orderBy, int? limit)
        {
            days = Math.Clamp(days <= 0 ? 30 : days, 1, 90);
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var fromUtc = DateTime.UtcNow.AddDays(-days);

            var query =
                from r in _dataProvider.GetTable<ProductReview>()
                join p in _dataProvider.GetTable<Product>() on r.ProductId equals p.Id
                join c in _dataProvider.GetTable<Customer>() on r.CustomerId equals c.Id
                where r.CreatedOnUtc >= fromUtc
                select new { r, p, c };

            if (vendorId.HasValue)
                query = query.Where(x => x.p.VendorId == vendorId.Value);
            if (customerId.HasValue)
                query = query.Where(x => x.r.CustomerId == customerId.Value);
            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var email = customerEmail.Trim().ToLower();
                query = query.Where(x => x.c.Email.ToLower() == email);
            }

            query = orderBy?.ToLowerInvariant() switch
            {
                "rating" => query.OrderByDescending(x => x.r.Rating).ThenByDescending(x => x.r.CreatedOnUtc),
                "helpful" => query.OrderByDescending(x => x.r.HelpfulYesTotal).ThenByDescending(x => x.r.CreatedOnUtc),
                _ => query.OrderByDescending(x => x.r.CreatedOnUtc)
            };

            var rows = await query.Take(take).Select(x => new ReviewSlim
            {
                CreatedOnUtc = x.r.CreatedOnUtc,
                ProductName = x.p.Name,
                VendorId = x.p.VendorId,
                CustomerId = x.r.CustomerId,
                Email = x.c.Email,
                Rating = x.r.Rating,
                IsApproved = x.r.IsApproved,
                Title = x.r.Title,
                ReviewText = x.r.ReviewText,
                TriagedByCustomerId = x.r.TriagedByCustomerId,
                TriagedOnUtc = x.r.TriagedOnUtc,
                ResolutionDetails = x.r.ResolutionDetails
            }).ToListAsync();

            var names = await ResolveCustomerNamesAsync(rows.Select(x => x.CustomerId).Distinct().ToList());
            var triagerEmails = await ResolveCustomerEmailsAsync(
                rows.Where(x => x.TriagedByCustomerId.HasValue).Select(x => x.TriagedByCustomerId.Value).Distinct().ToList());

            var result = new InsightsReportResult
            {
                Id = "reviews",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Date", Type = "date" },
                    new InsightsReportColumn { Name = "Product", Type = "string" },
                    new InsightsReportColumn { Name = "VendorId", Type = "number" },
                    new InsightsReportColumn { Name = "CustomerId", Type = "number" },
                    new InsightsReportColumn { Name = "Customer", Type = "string" },
                    new InsightsReportColumn { Name = "Email", Type = "string" },
                    new InsightsReportColumn { Name = "Rating", Type = "number" },
                    new InsightsReportColumn { Name = "Approved", Type = "string" },
                    new InsightsReportColumn { Name = "Title", Type = "string" },
                    new InsightsReportColumn { Name = "Review", Type = "string" },
                    new InsightsReportColumn { Name = "TriagedBy", Type = "string" },
                    new InsightsReportColumn { Name = "TriagedOn", Type = "date" },
                    new InsightsReportColumn { Name = "TriageHours", Type = "number" },
                    new InsightsReportColumn { Name = "Resolution", Type = "string" }
                }
            };

            foreach (var x in rows)
            {
                var triagedBy = "";
                if (x.TriagedByCustomerId.HasValue)
                    triagedBy = triagerEmails.TryGetValue(x.TriagedByCustomerId.Value, out var te) && !string.IsNullOrEmpty(te)
                        ? te
                        : x.TriagedByCustomerId.Value.ToString();

                object triageHours = null;
                if (x.TriagedOnUtc.HasValue)
                    triageHours = Math.Round((x.TriagedOnUtc.Value - x.CreatedOnUtc).TotalHours, 1);

                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Date"] = x.CreatedOnUtc.ToString("yyyy-MM-dd"),
                    ["Product"] = x.ProductName,
                    ["VendorId"] = x.VendorId,
                    ["CustomerId"] = x.CustomerId,
                    ["Customer"] = names.TryGetValue(x.CustomerId, out var n) ? n : "",
                    ["Email"] = x.Email,
                    ["Rating"] = x.Rating,
                    ["Approved"] = x.IsApproved ? "Yes" : "No",
                    ["Title"] = x.Title,
                    ["Review"] = Truncate(x.ReviewText, 200),
                    ["TriagedBy"] = triagedBy,
                    ["TriagedOn"] = x.TriagedOnUtc?.ToString("yyyy-MM-dd") ?? "",
                    ["TriageHours"] = triageHours,
                    ["Resolution"] = Truncate(x.ResolutionDetails, 200)
                });
            }

            return result;
        }

        /// <summary>Full names (First + Last) for the given customers, from GenericAttribute.</summary>
        private async Task<Dictionary<int, string>> ResolveCustomerNamesAsync(IList<int> customerIds)
        {
            var result = new Dictionary<int, string>();
            if (customerIds == null || customerIds.Count == 0)
                return result;

            var attrs = await _dataProvider.GetTable<GenericAttribute>()
                .Where(a => a.KeyGroup == "Customer"
                            && (a.Key == "FirstName" || a.Key == "LastName")
                            && customerIds.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.Key, a.Value })
                .ToListAsync();

            foreach (var g in attrs.GroupBy(a => a.EntityId))
            {
                var first = g.FirstOrDefault(a => a.Key == "FirstName")?.Value ?? "";
                var last = g.FirstOrDefault(a => a.Key == "LastName")?.Value ?? "";
                var full = $"{first} {last}".Trim();
                if (!string.IsNullOrEmpty(full))
                    result[g.Key] = full;
            }

            return result;
        }

        /// <summary>Emails for the given customers (used to name who triaged a review).</summary>
        private async Task<Dictionary<int, string>> ResolveCustomerEmailsAsync(IList<int> customerIds)
        {
            var result = new Dictionary<int, string>();
            if (customerIds == null || customerIds.Count == 0)
                return result;

            var list = await _dataProvider.GetTable<Customer>()
                .Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Email })
                .ToListAsync();

            foreach (var c in list)
                result[c.Id] = c.Email;

            return result;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        private async Task<List<OrderSlim>> LoadOrdersAsync(DateTime fromUtc, DateTime toUtc)
        {
            var rows = await _dataProvider.GetTable<Order>()
                .Where(o => !o.Deleted && o.CreatedOnUtc >= fromUtc && o.CreatedOnUtc <= toUtc)
                .Select(o => new OrderSlim
                {
                    CreatedOnUtc = o.CreatedOnUtc,
                    OrderStatusId = o.OrderStatusId,
                    OrderTotal = o.OrderTotal
                })
                .ToListAsync();
            return rows;
        }

        /// <summary>Resolve an int parameter from the caller's values, falling back to the declared default and clamping to [Min, Max].</summary>
        private static int ResolveInt(InsightsReportMeta meta, string name, IDictionary<string, string> parameters)
        {
            var def = meta.Parameters.FirstOrDefault(p => p.Name == name);
            if (def == null)
                return 0;

            var value = def.Default;
            if (parameters != null && parameters.TryGetValue(name, out var raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                value = parsed;

            return Math.Clamp(value, def.Min, def.Max);
        }

        private sealed class OrderSlim
        {
            public DateTime CreatedOnUtc { get; set; }
            public int OrderStatusId { get; set; }
            public decimal OrderTotal { get; set; }
        }

        private sealed class ReviewSlim
        {
            public DateTime CreatedOnUtc { get; set; }
            public string ProductName { get; set; }
            public int VendorId { get; set; }
            public int CustomerId { get; set; }
            public string Email { get; set; }
            public int Rating { get; set; }
            public bool IsApproved { get; set; }
            public string Title { get; set; }
            public string ReviewText { get; set; }
            public int? TriagedByCustomerId { get; set; }
            public DateTime? TriagedOnUtc { get; set; }
            public string ResolutionDetails { get; set; }
        }
    }
}
