using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB.Data;
using Nop.Data;

namespace Nop.Plugin.Company.Company.Services.Reporting
{
    /// <summary>
    /// Reporting widgets. Canonical rules (see docs/plans/2026-06-14-redash-query-audit.md):
    ///  - delivery time = ScheduleDate (nvarchar, UTC); local = +4h (Armenia UTC+4).
    ///    NEVER ScheduleDateTime (that's a copy of CreatedOnUtc). TRY_CONVERT guards the string.
    ///  - "real order" filter: o.Deleted = 0 AND o.OrderStatusId &lt;&gt; 40.
    ///  - vendors: v.Deleted = 0 only (no Active check, no test-vendor name exclusion).
    ///  - gift-card orders are NOT excluded (included everywhere).
    /// </summary>
    public class ReportService : IReportService
    {
        private readonly INopDataProvider _dataProvider;

        public ReportService(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        // Local (UTC+4) delivery datetime expression, reused across widgets.
        private const string LOCAL_DT = "DATEADD(HOUR, 4, TRY_CONVERT(datetime2, o.ScheduleDate))";
        private const string ORDER_FILTER =
            "v.Deleted = 0 AND o.Deleted = 0 AND o.OrderStatusId <> 40 " +
            "AND TRY_CONVERT(datetime2, o.ScheduleDate) IS NOT NULL";

        private static readonly List<WidgetDefinition> Catalog = new()
        {
            new() { Id = "order-total-per-vendor-this-week", Title = "Order total per vendor (this week)", Viz = WidgetViz.Table, Params = WidgetParams.None },
            new() { Id = "order-total-per-vendor",           Title = "Order total per vendor",            Viz = WidgetViz.Table, Params = WidgetParams.DateRange },
            new() { Id = "order-total-per-vendor-per-day",   Title = "Order total per vendor per day",    Viz = WidgetViz.Bar,   Params = WidgetParams.DateRange },
            new() { Id = "average-cheque",                   Title = "Average cheque",                    Viz = WidgetViz.Line,  Params = WidgetParams.DateRange },
            new() { Id = "active-persons",                   Title = "Active persons",                    Viz = WidgetViz.Bar,   Params = WidgetParams.DateRange },
            new() { Id = "order-list",                       Title = "Order list",                        Viz = WidgetViz.Table, Params = WidgetParams.DateRange }
        };

        public IList<WidgetDefinition> ListWidgets(bool includeAdmin)
        {
            return Catalog.Where(w => includeAdmin || !w.AdminOnly).ToList();
        }

        public async Task<ReportResult> RunWidgetAsync(string widgetId, DateTime? from, DateTime? to, bool isAdmin)
        {
            var def = Catalog.FirstOrDefault(w => w.Id == widgetId
                && (isAdmin || !w.AdminOnly))
                ?? throw new ArgumentException($"Unknown widget '{widgetId}'");

            // Default range = last 30 local days for DateRange widgets.
            if (def.Params == WidgetParams.DateRange)
            {
                var localToday = DateTime.UtcNow.AddHours(4).Date;
                to ??= localToday;
                from ??= localToday.AddDays(-30);
            }

            return def.Id switch
            {
                "order-total-per-vendor-this-week" => ToResult(def, await ThisWeekAsync()),
                "order-total-per-vendor"           => ToResult(def, await PerVendorAsync(from.Value, to.Value)),
                "order-total-per-vendor-per-day"   => ToResult(def, await PerVendorPerDayAsync(from.Value, to.Value)),
                "average-cheque"                   => ToResult(def, await AverageChequeAsync(from.Value, to.Value)),
                "active-persons"                   => ToResult(def, await ActivePersonsAsync(from.Value, to.Value)),
                "order-list"                       => ToResult(def, await OrderListAsync(from.Value, to.Value)),
                _ => throw new ArgumentException($"Widget '{widgetId}' not implemented")
            };
        }

        #region Widget queries

        private Task<IList<VendorWeekRow>> ThisWeekAsync()
        {
            var sql = $@"
SET DATEFIRST 1;
DECLARE @ws date = CAST(DATEADD(DAY, 1 - DATEPART(WEEKDAY, DATEADD(HOUR,4,GETUTCDATE())), DATEADD(HOUR,4,GETUTCDATE())) AS date);
SELECT Vendor,
  SUM(CASE WHEN ld = @ws                   THEN price ELSE 0 END) AS Monday,
  SUM(CASE WHEN ld = DATEADD(day,1,@ws)    THEN price ELSE 0 END) AS Tuesday,
  SUM(CASE WHEN ld = DATEADD(day,2,@ws)    THEN price ELSE 0 END) AS Wednesday,
  SUM(CASE WHEN ld = DATEADD(day,3,@ws)    THEN price ELSE 0 END) AS Thursday,
  SUM(CASE WHEN ld = DATEADD(day,4,@ws)    THEN price ELSE 0 END) AS Friday,
  SUM(CASE WHEN ld = DATEADD(day,5,@ws)    THEN price ELSE 0 END) AS Saturday,
  SUM(CASE WHEN ld = DATEADD(day,6,@ws)    THEN price ELSE 0 END) AS Sunday,
  SUM(price) AS Total
FROM (
  SELECT v.Name AS Vendor, v.Id AS VendorId, oi.PriceInclTax AS price,
         CAST({LOCAL_DT} AS date) AS ld
  FROM dbo.OrderItem oi
  JOIN dbo.[Order] o ON o.Id = oi.OrderId
  JOIN dbo.Product p ON p.Id = oi.ProductId
  JOIN dbo.Vendor v ON v.Id = p.VendorId
  WHERE {ORDER_FILTER}
    AND CAST({LOCAL_DT} AS date) >= @ws
    AND CAST({LOCAL_DT} AS date) <  DATEADD(day,7,@ws)
) t
GROUP BY Vendor, VendorId
ORDER BY VendorId;";
            return _dataProvider.QueryAsync<VendorWeekRow>(sql);
        }

        private Task<IList<VendorTotalRow>> PerVendorAsync(DateTime from, DateTime to)
        {
            var sql = $@"
SELECT v.Name AS Vendor, SUM(oi.Quantity) AS Quantity, SUM(oi.PriceInclTax) AS Total
FROM dbo.OrderItem oi
JOIN dbo.[Order] o ON o.Id = oi.OrderId
JOIN dbo.Product p ON p.Id = oi.ProductId
JOIN dbo.Vendor v ON v.Id = p.VendorId
WHERE {ORDER_FILTER}
  AND CAST({LOCAL_DT} AS date) BETWEEN @from AND @to
GROUP BY v.Name
ORDER BY Total DESC;";
            return _dataProvider.QueryAsync<VendorTotalRow>(sql, P(from, to));
        }

        private Task<IList<VendorDayRow>> PerVendorPerDayAsync(DateTime from, DateTime to)
        {
            // long-form (Vendor, Day, Total); pivot client-side — avoids the dynamic
            // SQL + global temp table (##) concurrency bug in the old Redash query.
            var sql = $@"
SELECT v.Name AS Vendor, CAST({LOCAL_DT} AS date) AS Day, SUM(oi.PriceInclTax) AS Total
FROM dbo.OrderItem oi
JOIN dbo.[Order] o ON o.Id = oi.OrderId
JOIN dbo.Product p ON p.Id = oi.ProductId
JOIN dbo.Vendor v ON v.Id = p.VendorId
WHERE {ORDER_FILTER}
  AND CAST({LOCAL_DT} AS date) BETWEEN @from AND @to
GROUP BY v.Name, CAST({LOCAL_DT} AS date)
ORDER BY Day, Vendor;";
            return _dataProvider.QueryAsync<VendorDayRow>(sql, P(from, to));
        }

        private Task<IList<AvgChequeRow>> AverageChequeAsync(DateTime from, DateTime to)
        {
            var sql = $@"
SELECT CAST({LOCAL_DT} AS date) AS Day, AVG(o.OrderTotal) AS AvgCheque
FROM dbo.[Order] o
WHERE o.Deleted = 0 AND o.OrderStatusId <> 40
  AND TRY_CONVERT(datetime2, o.ScheduleDate) IS NOT NULL
  AND CAST({LOCAL_DT} AS date) BETWEEN @from AND @to
GROUP BY CAST({LOCAL_DT} AS date)
ORDER BY Day;";
            return _dataProvider.QueryAsync<AvgChequeRow>(sql, P(from, to));
        }

        private Task<IList<ActivePersonsRow>> ActivePersonsAsync(DateTime from, DateTime to)
        {
            var sql = $@"
;WITH oc AS (
  SELECT o.CustomerId, COUNT(*) AS c
  FROM dbo.[Order] o
  WHERE o.Deleted = 0 AND o.OrderStatusId <> 40
    AND TRY_CONVERT(datetime2, o.ScheduleDate) IS NOT NULL
    AND CAST({LOCAL_DT} AS date) BETWEEN @from AND @to
  GROUP BY o.CustomerId
)
SELECT c AS OrderCount, COUNT(DISTINCT CustomerId) AS UniqueCustomers
FROM oc GROUP BY c ORDER BY c;";
            return _dataProvider.QueryAsync<ActivePersonsRow>(sql, P(from, to));
        }

        private Task<IList<OrderListRow>> OrderListAsync(DateTime from, DateTime to)
        {
            var sql = $@"
SELECT
  CONCAT(
    (SELECT TOP(1) [Value] FROM dbo.GenericAttribute WHERE EntityId = o.CustomerId AND [Key]='FirstName' AND KeyGroup='Customer'),
    ' ',
    (SELECT TOP(1) [Value] FROM dbo.GenericAttribute WHERE EntityId = o.CustomerId AND [Key]='LastName'  AND KeyGroup='Customer')
  ) AS Employee,
  CAST({LOCAL_DT} AS date) AS OrderDate,
  FORMAT({LOCAL_DT}, 'HH:mm') AS DeliveryHour,
  c.Email AS Email,
  o.OrderTotal AS OrderTotal
FROM dbo.[Order] o
LEFT JOIN dbo.Customer c ON c.Id = o.CustomerId
WHERE o.Deleted = 0 AND o.OrderStatusId <> 40
  AND TRY_CONVERT(datetime2, o.ScheduleDate) IS NOT NULL
  AND CAST({LOCAL_DT} AS date) BETWEEN @from AND @to
ORDER BY OrderDate DESC, Email;";
            return _dataProvider.QueryAsync<OrderListRow>(sql, P(from, to));
        }

        #endregion

        #region Helpers

        private static DataParameter[] P(DateTime from, DateTime to) => new[]
        {
            new DataParameter("from", from.Date),
            new DataParameter("to", to.Date)
        };

        private static ReportResult ToResult<T>(WidgetDefinition def, IList<T> rows)
        {
            var props = typeof(T).GetProperties();
            var result = new ReportResult { WidgetId = def.Id, Title = def.Title, Viz = def.Viz };
            foreach (var p in props)
                result.Columns.Add(p.Name);
            foreach (var row in rows)
                result.Rows.Add(props.Select(p => p.GetValue(row)).ToArray());
            return result;
        }

        #endregion
    }
}
