using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB.Data;
using Nop.Core;
using Nop.Data;
using Nop.Services.Companies;
using Nop.Services.Helpers;
using TimeZoneConverter;

namespace Nop.Plugin.Company.Company.Services.Reporting
{
    /// <summary>
    /// Reporting widgets. Canonical rules (see docs/plans/2026-06-14-redash-query-audit.md):
    ///  - delivery time = ScheduleDate (nvarchar, UTC); local = +tenant offset. The offset
    ///    is NOT hardcoded — it comes from the viewing customer's Company.TimeZone (same
    ///    source DeliveryTimeService uses), passed to SQL as @tzoffset minutes.
    ///    NEVER ScheduleDateTime (that's a copy of CreatedOnUtc). TRY_CONVERT guards the string.
    ///  - "real order" filter: o.Deleted = 0 AND o.OrderStatusId &lt;&gt; 40.
    ///  - vendors: v.Deleted = 0 only (no Active check, no test-vendor name exclusion).
    ///  - gift-card orders are NOT excluded (included everywhere).
    /// </summary>
    public class ReportService : IReportService
    {
        private readonly INopDataProvider _dataProvider;
        private readonly IWorkContext _workContext;
        private readonly ICompanyService _companyService;
        private readonly IDateTimeHelper _dateTimeHelper;

        public ReportService(
            INopDataProvider dataProvider,
            IWorkContext workContext,
            ICompanyService companyService,
            IDateTimeHelper dateTimeHelper)
        {
            _dataProvider = dataProvider;
            _workContext = workContext;
            _companyService = companyService;
            _dateTimeHelper = dateTimeHelper;
        }

        // Local delivery datetime = ScheduleDate (UTC) shifted by the tenant offset (minutes).
        private const string LOCAL_DT = "DATEADD(MINUTE, @tzoffset, TRY_CONVERT(datetime2, o.ScheduleDate))";
        private const string ORDER_FILTER =
            "v.Deleted = 0 AND o.Deleted = 0 AND o.OrderStatusId <> 40 " +
            "AND TRY_CONVERT(datetime2, o.ScheduleDate) IS NOT NULL";

        private static readonly List<WidgetDefinition> Catalog = new()
        {
            new() { Id = "order-total-per-vendor",           Title = "Order total per vendor",            Viz = WidgetViz.Table, Params = WidgetParams.DateRange,
                Description = "Total amount and item quantity per vendor over the selected delivery-date range." },
            new() { Id = "order-total-per-vendor-per-day",   Title = "Order total per vendor per day",    Viz = WidgetViz.Bar,   Params = WidgetParams.DateRange,
                Description = "Daily order total per vendor over the selected range (one row per vendor per delivery day)." },
            new() { Id = "average-cheque",                   Title = "Average cheque",                    Viz = WidgetViz.Line,  Params = WidgetParams.DateRange,
                Description = "Average order value per delivery day over the selected range." },
            new() { Id = "active-persons",                   Title = "Active persons",                    Viz = WidgetViz.Bar,   Params = WidgetParams.DateRange,
                Description = "Distribution of customers by number of orders placed in the selected range." },
            new() { Id = "order-list",                       Title = "Order list",                        Viz = WidgetViz.Table, Params = WidgetParams.DateRange,
                Description = "Every order in the selected delivery-date range: employee, date, delivery time, email, total." }
        };

        public IList<WidgetDefinition> ListWidgets(bool includeAdmin)
        {
            return Catalog.Where(w => includeAdmin || !w.AdminOnly).ToList();
        }

        /// <summary>UTC offset (minutes) for the current customer's company timezone.</summary>
        public async Task<int> GetUtcOffsetMinutesAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var company = customer == null ? null : await _companyService.GetCompanyByCustomerIdAsync(customer.Id);

            TimeZoneInfo tz;
            if (company != null && !string.IsNullOrEmpty(company.TimeZone))
                tz = TZConvert.GetTimeZoneInfo(company.TimeZone);
            else
                tz = await _dateTimeHelper.GetCustomerTimeZoneAsync(customer);

            return (int)tz.GetUtcOffset(DateTime.UtcNow).TotalMinutes;
        }

        public async Task<ReportResult> RunWidgetAsync(string widgetId, DateTime? from, DateTime? to, bool isAdmin)
        {
            var def = Catalog.FirstOrDefault(w => w.Id == widgetId && (isAdmin || !w.AdminOnly))
                ?? throw new ArgumentException($"Unknown widget '{widgetId}'");

            var tz = await GetUtcOffsetMinutesAsync();

            if (def.Params == WidgetParams.DateRange)
            {
                var localToday = DateTime.UtcNow.AddMinutes(tz).Date;
                to ??= localToday;
                from ??= localToday.AddDays(-7);
            }

            return def.Id switch
            {
                "order-total-per-vendor"         => ToResult(def, await PerVendorAsync(tz, from.Value, to.Value)),
                "order-total-per-vendor-per-day" => ToResult(def, await PerVendorPerDayAsync(tz, from.Value, to.Value)),
                "average-cheque"                 => ToResult(def, await AverageChequeAsync(tz, from.Value, to.Value)),
                "active-persons"                 => ToResult(def, await ActivePersonsAsync(tz, from.Value, to.Value)),
                "order-list"                     => ToResult(def, await OrderListAsync(tz, from.Value, to.Value)),
                _ => throw new ArgumentException($"Widget '{widgetId}' not implemented")
            };
        }

        #region Widget queries

        private Task<IList<VendorTotalRow>> PerVendorAsync(int tz, DateTime from, DateTime to)
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
            return _dataProvider.QueryAsync<VendorTotalRow>(sql, P(tz, from, to));
        }

        private Task<IList<VendorDayRow>> PerVendorPerDayAsync(int tz, DateTime from, DateTime to)
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
            return _dataProvider.QueryAsync<VendorDayRow>(sql, P(tz, from, to));
        }

        private Task<IList<AvgChequeRow>> AverageChequeAsync(int tz, DateTime from, DateTime to)
        {
            var sql = $@"
SELECT CAST({LOCAL_DT} AS date) AS Day, AVG(o.OrderTotal) AS AvgCheque
FROM dbo.[Order] o
WHERE o.Deleted = 0 AND o.OrderStatusId <> 40
  AND TRY_CONVERT(datetime2, o.ScheduleDate) IS NOT NULL
  AND CAST({LOCAL_DT} AS date) BETWEEN @from AND @to
GROUP BY CAST({LOCAL_DT} AS date)
ORDER BY Day;";
            return _dataProvider.QueryAsync<AvgChequeRow>(sql, P(tz, from, to));
        }

        private Task<IList<ActivePersonsRow>> ActivePersonsAsync(int tz, DateTime from, DateTime to)
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
            return _dataProvider.QueryAsync<ActivePersonsRow>(sql, P(tz, from, to));
        }

        private Task<IList<OrderListRow>> OrderListAsync(int tz, DateTime from, DateTime to)
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
            return _dataProvider.QueryAsync<OrderListRow>(sql, P(tz, from, to));
        }

        #endregion

        #region Helpers

        private static DataParameter[] P(int tz, DateTime from, DateTime to) => new[]
        {
            new DataParameter("tzoffset", tz),
            new DataParameter("from", from.Date),
            new DataParameter("to", to.Date)
        };

        private static ReportResult ToResult<T>(WidgetDefinition def, IList<T> rows)
        {
            var props = typeof(T).GetProperties();
            var result = new ReportResult { WidgetId = def.Id, Title = def.Title, Description = def.Description, Viz = def.Viz };
            foreach (var p in props)
                result.Columns.Add(p.Name);
            foreach (var row in rows)
                result.Rows.Add(props.Select(p => p.GetValue(row)).ToArray());
            return result;
        }

        #endregion
    }
}
