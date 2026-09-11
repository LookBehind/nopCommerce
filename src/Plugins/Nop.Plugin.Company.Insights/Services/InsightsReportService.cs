using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// P1 reports. Each pulls a bounded window of orders via LINQ2DB (provider-agnostic —
    /// works on SQL Server prod and PostgreSQL dev alike) and aggregates in memory, so there
    /// is no raw provider-specific SQL. Real Redash-audit reports get ported in later.
    /// </summary>
    public class InsightsReportService : IInsightsReportService
    {
        private readonly INopDataProvider _dataProvider;

        public InsightsReportService(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public IList<InsightsReportMeta> GetCatalog()
        {
            return new List<InsightsReportMeta>
            {
                new InsightsReportMeta
                {
                    Id = "orders-per-day",
                    Name = "Orders per day",
                    Description = "Order count and revenue by day (last 30 days).",
                    DefaultChart = "line",
                    XField = "Date",
                    YField = "Orders"
                },
                new InsightsReportMeta
                {
                    Id = "orders-by-status",
                    Name = "Orders by status",
                    Description = "Order count grouped by order status (last 90 days).",
                    DefaultChart = "pie",
                    CategoryField = "Status",
                    YField = "Orders"
                }
            };
        }

        public async Task<InsightsReportResult> RunAsync(string id, DateTime? fromUtc, DateTime? toUtc)
        {
            var now = DateTime.UtcNow;
            return id switch
            {
                "orders-per-day" => await OrdersPerDayAsync(fromUtc ?? now.Date.AddDays(-29), toUtc ?? now),
                "orders-by-status" => await OrdersByStatusAsync(fromUtc ?? now.Date.AddDays(-89), toUtc ?? now),
                _ => null
            };
        }

        public async Task<InsightsReportResult> QueryOrdersAsync(DateTime? fromUtc, DateTime? toUtc, string groupBy, string metric)
        {
            var now = DateTime.UtcNow;
            var orders = await LoadOrdersAsync(fromUtc ?? now.Date.AddDays(-29), toUtc ?? now);

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

        private async Task<InsightsReportResult> OrdersPerDayAsync(DateTime fromUtc, DateTime toUtc)
        {
            var orders = await LoadOrdersAsync(fromUtc, toUtc);

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

        private async Task<InsightsReportResult> OrdersByStatusAsync(DateTime fromUtc, DateTime toUtc)
        {
            var orders = await LoadOrdersAsync(fromUtc, toUtc);

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

        private sealed class OrderSlim
        {
            public DateTime CreatedOnUtc { get; set; }
            public int OrderStatusId { get; set; }
            public decimal OrderTotal { get; set; }
        }
    }
}
