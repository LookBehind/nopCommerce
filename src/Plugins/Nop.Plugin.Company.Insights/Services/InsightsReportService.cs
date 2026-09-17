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
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Vendors;
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
                    Description = "Recent product reviews (best viewed as a table). The agent can also filter by vendor/customer email or name and reorder via list_reviews.",
                    DefaultChart = "bar",
                    XField = "Rating",
                    YField = "Rating",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "days", Label = "Look-back (days)", Default = 30, Min = 1, Max = 90 },
                        new InsightsReportParam { Name = "limit", Label = "Max rows", Default = 50, Min = 1, Max = 200 }
                    }
                },
                new InsightsReportMeta
                {
                    Id = "vendor-traction",
                    Name = "Vendor traction",
                    Description = "Order items per vendor over time — who's gaining vs losing traction (best as an area chart).",
                    DefaultChart = "area",
                    XField = "Date",
                    YField = "OrderItems",
                    CategoryField = "Vendor",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "days", Label = "Look-back (days)", Default = 90, Min = 7, Max = 365 }
                    }
                },
                new InsightsReportMeta
                {
                    Id = "products-per-category",
                    Name = "Products per category",
                    Description = "Number of published products in each category.",
                    DefaultChart = "bar",
                    XField = "Category",
                    YField = "Products"
                },
                new InsightsReportMeta
                {
                    Id = "products-per-category-per-vendor",
                    Name = "Products per category · vendor",
                    Description = "Products per category broken down by vendor — spot over-crowded categories / competition (stacked bar).",
                    DefaultChart = "bar",
                    XField = "Category",
                    YField = "Products",
                    CategoryField = "Vendor"
                },
                new InsightsReportMeta
                {
                    Id = "delivery-order-items",
                    Name = "Ordered items by delivery date/time",
                    Description = "Quantity ordered per product per vendor for a delivery date/time or range, with a grand total.",
                    DefaultChart = "bar",
                    XField = "Product",
                    YField = "Quantity",
                    CategoryField = "Vendor",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "range", Label = "Delivery date/time", Type = "daterange", Default = 0, Min = 0, Max = 0 }
                    }
                },
                new InsightsReportMeta
                {
                    Id = "vendor-delivery-reliability",
                    Name = "Vendor delivery reliability",
                    Description = "Per vendor: on-time % and average delay (actual delivery vs the promised delivery time).",
                    DefaultChart = "bar",
                    XField = "Vendor",
                    YField = "On-time %",
                    Parameters = new List<InsightsReportParam>
                    {
                        new InsightsReportParam { Name = "days", Label = "Look-back (days)", Default = 90, Min = 7, Max = 365 }
                    }
                }
            };
        }

        private readonly INopDataProvider _dataProvider;
        private readonly Nop.Services.Media.IPictureService _pictureService;

        public InsightsReportService(INopDataProvider dataProvider, Nop.Services.Media.IPictureService pictureService)
        {
            _dataProvider = dataProvider;
            _pictureService = pictureService;
        }

        public async Task<InsightsReportResult> RunAsync(string id, IDictionary<string, string> parameters, ReportScope scope = null)
        {
            var meta = GetCatalog().FirstOrDefault(r => r.Id == id);
            if (meta == null)
                return null;

            var days = ResolveInt(meta, "days", parameters);

            return id switch
            {
                "orders-per-day" => await OrdersPerDayAsync(days, scope),
                "orders-by-status" => await OrdersByStatusAsync(days, scope),
                "reviews" => await GetReviewsAsync(days, null, null, null, "date", ResolveInt(meta, "limit", parameters), scope),
                "vendor-traction" => await VendorTractionAsync(days, scope),
                "products-per-category" => await ProductsPerCategoryAsync(scope),
                "products-per-category-per-vendor" => await ProductsPerCategoryPerVendorAsync(scope),
                "delivery-order-items" => await DeliveryOrderItemsAsync(parameters, scope),
                "vendor-delivery-reliability" => await VendorDeliveryReliabilityAsync(days, scope),
                _ => null
            };
        }

        public async Task<InsightsReportResult> QueryOrdersAsync(int days, string groupBy, string metric, ReportScope scope = null)
        {
            days = Math.Clamp(days <= 0 ? 30 : days, 1, QueryOrdersMaxDays);
            var orders = await LoadOrdersAsync(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow, scope);

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

        private async Task<InsightsReportResult> OrdersPerDayAsync(int days, ReportScope scope)
        {
            var orders = await LoadOrdersAsync(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow, scope);

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

        private async Task<InsightsReportResult> OrdersByStatusAsync(int days, ReportScope scope)
        {
            var orders = await LoadOrdersAsync(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow, scope);

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

        public async Task<InsightsReportResult> GetReviewsAsync(int days, int? vendorId, string customerEmail, string customerName, string orderBy, int? limit, ReportScope scope = null)
        {
            days = Math.Clamp(days <= 0 ? 30 : days, 1, 90);
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var fromUtc = DateTime.UtcNow.AddDays(-days);

            // Fail closed for a scoped profile with no resolvable company.
            if (scope != null && scope.Denied)
                return EmptyReviews();

            // Company scope → restrict to that company's vendors' products.
            IList<int> scopedVendorIds = scope?.CompanyId != null ? (scope.VendorIds ?? new List<int>()) : null;
            if (scopedVendorIds != null && scopedVendorIds.Count == 0)
                return EmptyReviews(); // scoped company has no vendors

            // Optional customer-name filter → resolve matching customer ids up front.
            List<int> nameCustomerIds = null;
            if (!string.IsNullOrWhiteSpace(customerName))
            {
                nameCustomerIds = await ResolveCustomerIdsByNameAsync(customerName.Trim());
                if (nameCustomerIds.Count == 0)
                    return EmptyReviews();
            }

            var query =
                from r in _dataProvider.GetTable<ProductReview>()
                join p in _dataProvider.GetTable<Product>() on r.ProductId equals p.Id
                join c in _dataProvider.GetTable<Customer>() on r.CustomerId equals c.Id
                where r.CreatedOnUtc >= fromUtc
                select new { r, p, c };

            if (vendorId.HasValue)
                query = query.Where(x => x.p.VendorId == vendorId.Value);
            if (scopedVendorIds != null)
                query = query.Where(x => scopedVendorIds.Contains(x.p.VendorId));
            if (nameCustomerIds != null)
                query = query.Where(x => nameCustomerIds.Contains(x.r.CustomerId));
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
            var vendors = await ResolveVendorsAsync(rows.Select(x => x.VendorId).Distinct().ToList());
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
                    new InsightsReportColumn { Name = "Vendor", Type = "string" },
                    new InsightsReportColumn { Name = "VendorEmail", Type = "string" },
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
                    ["Vendor"] = vendors.TryGetValue(x.VendorId, out var vr) ? vr.Name : "",
                    ["VendorEmail"] = vendors.TryGetValue(x.VendorId, out var vre) ? vre.Email : "",
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

        // ---- Workplace Manager reports ----

        /// <summary>Order items per vendor over time (weekly buckets) — vendor traction. Top 10 vendors + "Other".</summary>
        private async Task<InsightsReportResult> VendorTractionAsync(int days, ReportScope scope)
        {
            days = Math.Clamp(days <= 0 ? 90 : days, 7, QueryOrdersMaxDays);
            var result = new InsightsReportResult
            {
                Id = "vendor-traction",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Date", Type = "date" },
                    new InsightsReportColumn { Name = "Vendor", Type = "string" },
                    new InsightsReportColumn { Name = "OrderItems", Type = "number" }
                }
            };
            if (scope != null && scope.Denied)
                return result;

            var fromUtc = DateTime.UtcNow.AddDays(-days);
            var query =
                from oi in _dataProvider.GetTable<OrderItem>()
                join o in _dataProvider.GetTable<Order>() on oi.OrderId equals o.Id
                join p in _dataProvider.GetTable<Product>() on oi.ProductId equals p.Id
                where !o.Deleted && o.CreatedOnUtc >= fromUtc
                select new { o.CreatedOnUtc, p.VendorId, o.CompanyId };

            if (scope?.CompanyId is int companyId)
                query = query.Where(x => x.CompanyId == companyId);

            // Per-vendor per-day counts in SQL, then bucket to weeks in memory.
            var daily = await query
                .Where(x => x.VendorId > 0)
                .GroupBy(x => new { x.VendorId, Day = x.CreatedOnUtc.Date })
                .Select(g => new { g.Key.VendorId, g.Key.Day, Count = g.Count() })
                .ToListAsync();

            if (daily.Count == 0)
                return result;

            var vendorNames = await ResolveVendorNamesAsync(daily.Select(d => d.VendorId).Distinct().ToList());

            // Top 10 vendors by total items; the rest folded into "Other".
            var totals = daily.GroupBy(d => d.VendorId)
                .Select(g => new { VendorId = g.Key, Total = g.Sum(x => x.Count) })
                .OrderByDescending(x => x.Total)
                .ToList();
            var topVendorIds = totals.Take(10).Select(x => x.VendorId).ToHashSet();

            string VendorLabel(int vid) =>
                topVendorIds.Contains(vid) ? (vendorNames.TryGetValue(vid, out var n) ? n : $"Vendor {vid}") : "Other";

            var weekly = daily
                .GroupBy(d => new { Week = StartOfWeek(d.Day), Vendor = VendorLabel(d.VendorId) })
                .Select(g => new { g.Key.Week, g.Key.Vendor, Count = g.Sum(x => x.Count) })
                .OrderBy(x => x.Week).ThenBy(x => x.Vendor);

            foreach (var w in weekly)
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Date"] = w.Week.ToString("yyyy-MM-dd"),
                    ["Vendor"] = w.Vendor,
                    ["OrderItems"] = w.Count
                });

            return result;
        }

        /// <summary>Published-product counts per category.</summary>
        private async Task<InsightsReportResult> ProductsPerCategoryAsync(ReportScope scope)
        {
            var result = new InsightsReportResult
            {
                Id = "products-per-category",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Category", Type = "string" },
                    new InsightsReportColumn { Name = "Products", Type = "number" }
                }
            };
            var rows = await LoadCatalogRowsAsync(scope);
            foreach (var g in rows.GroupBy(r => r.Category).OrderByDescending(g => g.Select(x => x.ProductId).Distinct().Count()))
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Category"] = g.Key,
                    ["Products"] = g.Select(x => x.ProductId).Distinct().Count()
                });
            return result;
        }

        /// <summary>Published-product counts per category, split by vendor (competition / crowdedness).</summary>
        private async Task<InsightsReportResult> ProductsPerCategoryPerVendorAsync(ReportScope scope)
        {
            var result = new InsightsReportResult
            {
                Id = "products-per-category-per-vendor",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Category", Type = "string" },
                    new InsightsReportColumn { Name = "Vendor", Type = "string" },
                    new InsightsReportColumn { Name = "VendorEmail", Type = "string" },
                    new InsightsReportColumn { Name = "Products", Type = "number" }
                }
            };
            var rows = await LoadCatalogRowsAsync(scope);
            if (rows.Count == 0)
                return result;

            var vendors = await ResolveVendorsAsync(rows.Select(r => r.VendorId).Distinct().ToList());
            foreach (var g in rows
                .Where(r => r.VendorId > 0)
                .GroupBy(r => new { r.Category, r.VendorId })
                .Select(g => new { g.Key.Category, g.Key.VendorId, Count = g.Select(x => x.ProductId).Distinct().Count() })
                .OrderBy(x => x.Category).ThenByDescending(x => x.Count))
            {
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Category"] = g.Category,
                    ["Vendor"] = vendors.TryGetValue(g.VendorId, out var vr) ? vr.Name : $"Vendor {g.VendorId}",
                    ["VendorEmail"] = vendors.TryGetValue(g.VendorId, out var vre) ? vre.Email : "",
                    ["Products"] = g.Count
                });
            }
            return result;
        }

        // ---- Backoffice reports ----

        /// <summary>Quantity ordered per product per vendor for a delivery date/time (or range), with a grand total.</summary>
        private async Task<InsightsReportResult> DeliveryOrderItemsAsync(IDictionary<string, string> parameters, ReportScope scope)
        {
            var result = new InsightsReportResult
            {
                Id = "delivery-order-items",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Vendor", Type = "string" },
                    new InsightsReportColumn { Name = "VendorEmail", Type = "string" },
                    new InsightsReportColumn { Name = "Product", Type = "string" },
                    new InsightsReportColumn { Name = "Quantity", Type = "number" }
                }
            };
            if (scope != null && scope.Denied)
                return result;

            // Delivery date range (ScheduleDate is UTC+4 wall-clock; compare directly). Default = today.
            var today = DateTime.UtcNow.Date;
            var fromDate = ParseDate(parameters, "from") ?? today;
            var toDate = ParseDate(parameters, "to") ?? fromDate;
            var toEnd = toDate.Date.AddDays(1).AddTicks(-1);
            var slot = parameters != null && parameters.TryGetValue("slot", out var s) ? (s ?? "").Trim() : "";

            var query =
                from oi in _dataProvider.GetTable<OrderItem>()
                join o in _dataProvider.GetTable<Order>() on oi.OrderId equals o.Id
                join p in _dataProvider.GetTable<Product>() on oi.ProductId equals p.Id
                where !o.Deleted && o.ScheduleDate >= fromDate && o.ScheduleDate <= toEnd
                select new { o.ScheduleDate, o.CompanyId, p.VendorId, ProductName = p.Name, oi.Quantity };

            if (scope?.CompanyId is int companyId)
                query = query.Where(x => x.CompanyId == companyId);

            var rows = await query.ToListAsync();

            // Optional delivery time-slot filter (HH:mm), applied in memory on the parsed DateTime.
            if (!string.IsNullOrEmpty(slot) && TimeSpan.TryParse(slot, out var ts))
                rows = rows.Where(x => x.ScheduleDate.Hour == ts.Hours && x.ScheduleDate.Minute == ts.Minutes).ToList();

            var vendors = await ResolveVendorsAsync(rows.Select(r => r.VendorId).Distinct().ToList());

            var grouped = rows
                .GroupBy(r => new { r.VendorId, r.ProductName })
                .Select(g => new { g.Key.VendorId, g.Key.ProductName, Qty = g.Sum(x => x.Quantity) })
                .OrderByDescending(x => x.Qty)
                .ToList();

            foreach (var g in grouped)
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Vendor"] = vendors.TryGetValue(g.VendorId, out var vr) ? vr.Name : $"Vendor {g.VendorId}",
                    ["VendorEmail"] = vendors.TryGetValue(g.VendorId, out var vre) ? vre.Email : "",
                    ["Product"] = g.ProductName,
                    ["Quantity"] = g.Qty
                });

            var rangeLabel = fromDate.Date == toDate.Date ? fromDate.ToString("yyyy-MM-dd") : $"{fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd}";
            if (!string.IsNullOrEmpty(slot))
                rangeLabel += $" @ {slot}";
            result.Totals = new List<InsightsReportTotal>
            {
                new InsightsReportTotal { Label = "Total items", Value = grouped.Sum(x => x.Qty) },
                new InsightsReportTotal { Label = "Distinct products", Value = grouped.Count },
                new InsightsReportTotal { Label = "Delivery", Value = rangeLabel }
            };
            return result;
        }

        /// <summary>
        /// Per-vendor delivery reliability: actual delivery (Shipment.DeliveryDateUtc, falling back to
        /// ShippedDateUtc) vs the promised Order.ScheduleDate. One delivery event per (shipment, vendor).
        /// </summary>
        private async Task<InsightsReportResult> VendorDeliveryReliabilityAsync(int days, ReportScope scope)
        {
            const int graceMinutes = 15; // delivered within 15 min of the promise counts as on-time
            days = Math.Clamp(days <= 0 ? 90 : days, 7, QueryOrdersMaxDays);
            var fromUtc = DateTime.UtcNow.AddDays(-days);

            var result = new InsightsReportResult
            {
                Id = "vendor-delivery-reliability",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "VendorId", Type = "number" },
                    new InsightsReportColumn { Name = "Vendor", Type = "string" },
                    new InsightsReportColumn { Name = "VendorEmail", Type = "string" },
                    new InsightsReportColumn { Name = "Deliveries", Type = "number" },
                    new InsightsReportColumn { Name = "On-time %", Type = "number" },
                    new InsightsReportColumn { Name = "Avg delay (h)", Type = "number" },
                    new InsightsReportColumn { Name = "Late", Type = "number" }
                }
            };
            if (scope != null && scope.Denied)
                return result;

            var query =
                from si in _dataProvider.GetTable<ShipmentItem>()
                join s in _dataProvider.GetTable<Shipment>() on si.ShipmentId equals s.Id
                join o in _dataProvider.GetTable<Order>() on s.OrderId equals o.Id
                join oi in _dataProvider.GetTable<OrderItem>() on si.OrderItemId equals oi.Id
                join p in _dataProvider.GetTable<Product>() on oi.ProductId equals p.Id
                where !o.Deleted && p.VendorId > 0
                    && (s.DeliveryDateUtc >= fromUtc || s.ShippedDateUtc >= fromUtc)
                select new { ShipmentId = s.Id, s.DeliveryDateUtc, s.ShippedDateUtc, o.ScheduleDate, o.CompanyId, p.VendorId };

            if (scope?.CompanyId is int companyId)
                query = query.Where(x => x.CompanyId == companyId);

            var raw = await query.ToListAsync();
            if (raw.Count == 0)
                return result;

            // One event per (shipment, vendor): a shipment counts once for each vendor it contains.
            var events = raw
                .Select(x => new
                {
                    x.ShipmentId,
                    x.VendorId,
                    Actual = x.DeliveryDateUtc ?? x.ShippedDateUtc,
                    Promised = x.ScheduleDate
                })
                .Where(x => x.Actual.HasValue && x.Actual.Value >= fromUtc)
                .GroupBy(x => new { x.ShipmentId, x.VendorId })
                .Select(g => new
                {
                    g.Key.VendorId,
                    LateMinutes = (g.First().Actual.Value - g.First().Promised).TotalMinutes
                })
                .ToList();

            if (events.Count == 0)
                return result;

            var vendors = await ResolveVendorsAsync(events.Select(e => e.VendorId).Distinct().ToList());

            var perVendor = events
                .GroupBy(e => e.VendorId)
                .Select(g =>
                {
                    var deliveries = g.Count();
                    var onTime = g.Count(x => x.LateMinutes <= graceMinutes);
                    var avgDelay = g.Average(x => Math.Max(x.LateMinutes, 0)) / 60.0;
                    return new
                    {
                        VendorId = g.Key,
                        Deliveries = deliveries,
                        OnTimePct = (int)Math.Round(100.0 * onTime / deliveries),
                        AvgDelayHours = Math.Round(avgDelay, 1),
                        Late = deliveries - onTime
                    };
                })
                .OrderBy(x => x.OnTimePct).ThenByDescending(x => x.AvgDelayHours)
                .ToList();

            foreach (var v in perVendor)
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["VendorId"] = v.VendorId,
                    ["Vendor"] = vendors.TryGetValue(v.VendorId, out var vr) ? vr.Name : $"Vendor {v.VendorId}",
                    ["VendorEmail"] = vendors.TryGetValue(v.VendorId, out var vre) ? vre.Email : "",
                    ["Deliveries"] = v.Deliveries,
                    ["On-time %"] = v.OnTimePct,
                    ["Avg delay (h)"] = v.AvgDelayHours,
                    ["Late"] = v.Late
                });

            var totalDeliveries = perVendor.Sum(x => x.Deliveries);
            var totalLate = perVendor.Sum(x => x.Late);
            result.Totals = new List<InsightsReportTotal>
            {
                new InsightsReportTotal { Label = "Deliveries", Value = totalDeliveries },
                new InsightsReportTotal { Label = "On-time %", Value = totalDeliveries > 0 ? (int)Math.Round(100.0 * (totalDeliveries - totalLate) / totalDeliveries) : 0 },
                new InsightsReportTotal { Label = "Late", Value = totalLate }
            };
            return result;
        }

        private sealed class CatalogRow
        {
            public string Category { get; set; }
            public int VendorId { get; set; }
            public int ProductId { get; set; }
        }

        // ---- Shared agent data tools (products / orders) ----

        /// <summary>Product lookup for the agents — filter by id/vendor/name/category, ordered, with short +
        /// full description, weight, SKU, price, categories and picture URLs. Company-scoped to the caller's vendors.</summary>
        public async Task<InsightsReportResult> ListProductsAsync(int? id, int? vendorId, string name, string category, string orderBy, int? limit, ReportScope scope = null)
        {
            var take = Math.Clamp(limit ?? 20, 1, 50);
            var result = new InsightsReportResult
            {
                Id = "products",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Id", Type = "number" },
                    new InsightsReportColumn { Name = "Name", Type = "string" },
                    new InsightsReportColumn { Name = "Vendor", Type = "string" },
                    new InsightsReportColumn { Name = "VendorEmail", Type = "string" },
                    new InsightsReportColumn { Name = "Sku", Type = "string" },
                    new InsightsReportColumn { Name = "Price", Type = "number" },
                    new InsightsReportColumn { Name = "Weight", Type = "number" },
                    new InsightsReportColumn { Name = "Published", Type = "string" },
                    new InsightsReportColumn { Name = "Categories", Type = "string" },
                    new InsightsReportColumn { Name = "ShortDescription", Type = "string" },
                    new InsightsReportColumn { Name = "FullDescription", Type = "string" },
                    new InsightsReportColumn { Name = "Pictures", Type = "string" }
                }
            };
            if (scope != null && scope.Denied)
                return result;
            IList<int> scopedVendorIds = scope?.CompanyId != null ? (scope.VendorIds ?? new List<int>()) : null;
            if (scopedVendorIds != null && scopedVendorIds.Count == 0)
                return result;

            var query = _dataProvider.GetTable<Product>().Where(p => !p.Deleted);
            if (id.HasValue) query = query.Where(p => p.Id == id.Value);
            if (vendorId.HasValue) query = query.Where(p => p.VendorId == vendorId.Value);
            if (scopedVendorIds != null) query = query.Where(p => scopedVendorIds.Contains(p.VendorId));
            if (!string.IsNullOrWhiteSpace(name))
            {
                var n = name.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(n));
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                if (int.TryParse(cat, out var catId))
                {
                    var pids = _dataProvider.GetTable<ProductCategory>().Where(pc => pc.CategoryId == catId).Select(pc => pc.ProductId);
                    query = query.Where(p => pids.Contains(p.Id));
                }
                else
                {
                    var cl = cat.ToLower();
                    var pids = from pc in _dataProvider.GetTable<ProductCategory>()
                               join c in _dataProvider.GetTable<Category>() on pc.CategoryId equals c.Id
                               where c.Name.ToLower().Contains(cl)
                               select pc.ProductId;
                    query = query.Where(p => pids.Contains(p.Id));
                }
            }

            query = orderBy?.ToLowerInvariant() switch
            {
                "name" => query.OrderBy(p => p.Name),
                "price" => query.OrderByDescending(p => p.Price),
                "created" => query.OrderByDescending(p => p.CreatedOnUtc),
                _ => query.OrderByDescending(p => p.Id)
            };

            var rows = await query.Take(take).Select(p => new ProductSlim
            {
                Id = p.Id, Name = p.Name, VendorId = p.VendorId, Sku = p.Sku, Price = p.Price, Weight = p.Weight,
                Published = p.Published, ShortDescription = p.ShortDescription, FullDescription = p.FullDescription
            }).ToListAsync();

            var vendors = await ResolveVendorsAsync(rows.Select(r => r.VendorId).Distinct().ToList());
            var cats = await ResolveCategoriesForProductsAsync(rows.Select(r => r.Id).ToList());

            foreach (var p in rows)
            {
                var pics = await BuildPictureUrlsAsync(p.Id);
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Id"] = p.Id,
                    ["Name"] = p.Name,
                    ["Vendor"] = vendors.TryGetValue(p.VendorId, out var vr) ? vr.Name : "",
                    ["VendorEmail"] = vendors.TryGetValue(p.VendorId, out var vre) ? vre.Email : "",
                    ["Sku"] = p.Sku,
                    ["Price"] = p.Price,
                    ["Weight"] = p.Weight,
                    ["Published"] = p.Published ? "Yes" : "No",
                    ["Categories"] = cats.TryGetValue(p.Id, out var cn) ? string.Join(", ", cn) : "",
                    ["ShortDescription"] = Truncate(StripHtml(p.ShortDescription), 400),
                    ["FullDescription"] = Truncate(StripHtml(p.FullDescription), 2000),
                    ["Pictures"] = string.Join(" ", pics)
                });
            }
            return result;
        }

        /// <summary>Order lookup for the agents — by delivery-date range and status, with customer name/email and
        /// an item summary. Company-scoped via Order.CompanyId.</summary>
        public async Task<InsightsReportResult> ListOrdersAsync(string from, string to, string status, int? limit, ReportScope scope = null)
        {
            var take = Math.Clamp(limit ?? 25, 1, 100);
            var result = new InsightsReportResult
            {
                Id = "orders",
                Columns = new List<InsightsReportColumn>
                {
                    new InsightsReportColumn { Name = "Id", Type = "number" },
                    new InsightsReportColumn { Name = "Created", Type = "date" },
                    new InsightsReportColumn { Name = "Delivery", Type = "date" },
                    new InsightsReportColumn { Name = "Status", Type = "string" },
                    new InsightsReportColumn { Name = "Total", Type = "number" },
                    new InsightsReportColumn { Name = "Customer", Type = "string" },
                    new InsightsReportColumn { Name = "Email", Type = "string" },
                    new InsightsReportColumn { Name = "Items", Type = "string" }
                }
            };
            if (scope != null && scope.Denied)
                return result;

            var today = DateTime.UtcNow.Date;
            var fromDate = ParseDateString(from) ?? today.AddDays(-30);
            var toDate = ParseDateString(to) ?? today.AddDays(1);
            var toEnd = toDate.Date.AddDays(1).AddTicks(-1);

            var query = _dataProvider.GetTable<Order>().Where(o => !o.Deleted && o.ScheduleDate >= fromDate && o.ScheduleDate <= toEnd);
            if (scope?.CompanyId is int cid)
                query = query.Where(o => o.CompanyId == cid);
            if (!string.IsNullOrWhiteSpace(status) && int.TryParse(status.Trim(), out var st))
                query = query.Where(o => o.OrderStatusId == st);

            var orders = await query.OrderByDescending(o => o.ScheduleDate).Take(take)
                .Select(o => new { o.Id, o.CreatedOnUtc, o.ScheduleDate, o.OrderStatusId, o.OrderTotal, o.CustomerId })
                .ToListAsync();

            var custIds = orders.Select(o => o.CustomerId).Distinct().ToList();
            var names = await ResolveCustomerNamesAsync(custIds);
            var emails = await ResolveCustomerEmailsAsync(custIds);
            var items = await ResolveOrderItemsSummaryAsync(orders.Select(o => o.Id).ToList());

            foreach (var o in orders)
                result.Rows.Add(new Dictionary<string, object>
                {
                    ["Id"] = o.Id,
                    ["Created"] = o.CreatedOnUtc.ToString("yyyy-MM-dd"),
                    ["Delivery"] = o.ScheduleDate.ToString("yyyy-MM-dd HH:mm"),
                    ["Status"] = ((OrderStatus)o.OrderStatusId).ToString(),
                    ["Total"] = o.OrderTotal,
                    ["Customer"] = names.TryGetValue(o.CustomerId, out var n) ? n : "",
                    ["Email"] = emails.TryGetValue(o.CustomerId, out var e) ? e : "",
                    ["Items"] = items.TryGetValue(o.Id, out var it) ? it : ""
                });
            return result;
        }

        private sealed class ProductSlim
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int VendorId { get; set; }
            public string Sku { get; set; }
            public decimal Price { get; set; }
            public decimal Weight { get; set; }
            public bool Published { get; set; }
            public string ShortDescription { get; set; }
            public string FullDescription { get; set; }
        }

        private async Task<Dictionary<int, List<string>>> ResolveCategoriesForProductsAsync(IList<int> productIds)
        {
            var map = new Dictionary<int, List<string>>();
            if (productIds == null || productIds.Count == 0)
                return map;
            var rows = await (from pc in _dataProvider.GetTable<ProductCategory>()
                              join c in _dataProvider.GetTable<Category>() on pc.CategoryId equals c.Id
                              where productIds.Contains(pc.ProductId)
                              orderby pc.DisplayOrder
                              select new { pc.ProductId, c.Name }).ToListAsync();
            foreach (var r in rows)
            {
                if (!map.TryGetValue(r.ProductId, out var l)) { l = new List<string>(); map[r.ProductId] = l; }
                l.Add(r.Name);
            }
            return map;
        }

        private async Task<List<string>> BuildPictureUrlsAsync(int productId)
        {
            var urls = new List<string>();
            try
            {
                var picIds = await _dataProvider.GetTable<ProductPicture>()
                    .Where(pp => pp.ProductId == productId).OrderBy(pp => pp.DisplayOrder)
                    .Select(pp => pp.PictureId).Take(5).ToListAsync();
                foreach (var pid in picIds)
                {
                    var url = await _pictureService.GetPictureUrlAsync(pid);
                    if (!string.IsNullOrWhiteSpace(url))
                        urls.Add(url);
                }
            }
            catch { /* pictures are best-effort */ }
            return urls;
        }

        private async Task<Dictionary<int, string>> ResolveOrderItemsSummaryAsync(IList<int> orderIds)
        {
            var map = new Dictionary<int, string>();
            if (orderIds == null || orderIds.Count == 0)
                return map;
            var rows = await (from oi in _dataProvider.GetTable<OrderItem>()
                              join p in _dataProvider.GetTable<Product>() on oi.ProductId equals p.Id
                              where orderIds.Contains(oi.OrderId)
                              select new { oi.OrderId, p.Name, oi.Quantity }).ToListAsync();
            foreach (var g in rows.GroupBy(r => r.OrderId))
                map[g.Key] = string.Join("; ", g.Select(x => $"{x.Name} ×{x.Quantity}"));
            return map;
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        }

        private static DateTime? ParseDateString(string s) =>
            !string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : (DateTime?)null;

        /// <summary>Published, non-deleted products mapped to categories, scoped to the company's vendors when scoped.</summary>
        private async Task<List<CatalogRow>> LoadCatalogRowsAsync(ReportScope scope)
        {
            if (scope != null && scope.Denied)
                return new List<CatalogRow>();

            IList<int> scopedVendorIds = scope?.CompanyId != null ? (scope.VendorIds ?? new List<int>()) : null;
            if (scopedVendorIds != null && scopedVendorIds.Count == 0)
                return new List<CatalogRow>();

            var query =
                from pc in _dataProvider.GetTable<ProductCategory>()
                join p in _dataProvider.GetTable<Product>() on pc.ProductId equals p.Id
                join cat in _dataProvider.GetTable<Category>() on pc.CategoryId equals cat.Id
                where !p.Deleted && p.Published && !cat.Deleted
                select new { Category = cat.Name, p.VendorId, ProductId = p.Id };

            if (scopedVendorIds != null)
                query = query.Where(x => scopedVendorIds.Contains(x.VendorId));

            var rows = await query.ToListAsync();
            return rows.Select(x => new CatalogRow { Category = x.Category, VendorId = x.VendorId, ProductId = x.ProductId }).ToList();
        }

        /// <summary>Vendor name + email for the given ids (every vendor-bearing report exposes both, never just the id).</summary>
        private async Task<Dictionary<int, VendorRef>> ResolveVendorsAsync(IList<int> vendorIds)
        {
            var result = new Dictionary<int, VendorRef>();
            var ids = vendorIds?.Where(v => v > 0).Distinct().ToList();
            if (ids == null || ids.Count == 0)
                return result;

            var list = await _dataProvider.GetTable<Vendor>()
                .Where(v => ids.Contains(v.Id))
                .Select(v => new { v.Id, v.Name, v.Email })
                .ToListAsync();
            foreach (var v in list)
                result[v.Id] = new VendorRef { Name = v.Name, Email = v.Email ?? "" };
            return result;
        }

        /// <summary>Vendor display names for the given ids (thin wrapper over <see cref="ResolveVendorsAsync"/>).</summary>
        private async Task<Dictionary<int, string>> ResolveVendorNamesAsync(IList<int> vendorIds)
        {
            var vendors = await ResolveVendorsAsync(vendorIds);
            return vendors.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        }

        private sealed class VendorRef
        {
            public string Name { get; set; }
            public string Email { get; set; }
        }

        private static DateTime StartOfWeek(DateTime d)
        {
            var diff = ((int)d.DayOfWeek + 6) % 7; // Monday = 0
            return d.Date.AddDays(-diff);
        }

        private static DateTime? ParseDate(IDictionary<string, string> parameters, string key)
        {
            if (parameters != null && parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
                && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            return null;
        }

        /// <summary>An empty reviews result carrying the correct column shape (for fail-closed / no-match).</summary>
        private static InsightsReportResult EmptyReviews() => new InsightsReportResult
        {
            Id = "reviews",
            Columns = new List<InsightsReportColumn>
            {
                new InsightsReportColumn { Name = "Date", Type = "date" },
                new InsightsReportColumn { Name = "Product", Type = "string" },
                new InsightsReportColumn { Name = "VendorId", Type = "number" },
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

        /// <summary>Customer ids whose first/last name (GenericAttribute) contains the given text (case-insensitive).</summary>
        private async Task<List<int>> ResolveCustomerIdsByNameAsync(string name)
        {
            var term = name.ToLower();
            var ids = await _dataProvider.GetTable<GenericAttribute>()
                .Where(a => a.KeyGroup == "Customer"
                            && (a.Key == "FirstName" || a.Key == "LastName")
                            && a.Value != null
                            && a.Value.ToLower().Contains(term))
                .Select(a => a.EntityId)
                .Distinct()
                .ToListAsync();
            return ids;
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

        private async Task<List<OrderSlim>> LoadOrdersAsync(DateTime fromUtc, DateTime toUtc, ReportScope scope)
        {
            // Fail closed: a scoped profile with no resolvable company sees nothing.
            if (scope != null && scope.Denied)
                return new List<OrderSlim>();

            var query = _dataProvider.GetTable<Order>()
                .Where(o => !o.Deleted && o.CreatedOnUtc >= fromUtc && o.CreatedOnUtc <= toUtc);

            if (scope?.CompanyId is int companyId)
                query = query.Where(o => o.CompanyId == companyId);

            return await query
                .Select(o => new OrderSlim
                {
                    CreatedOnUtc = o.CreatedOnUtc,
                    OrderStatusId = o.OrderStatusId,
                    OrderTotal = o.OrderTotal
                })
                .ToListAsync();
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
