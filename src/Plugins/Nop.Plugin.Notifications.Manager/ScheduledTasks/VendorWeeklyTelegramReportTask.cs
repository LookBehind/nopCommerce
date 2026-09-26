using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Services.Companies;
using Nop.Services.Helpers;
using Nop.Services.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TimeZoneConverter;
using ILogger = Nop.Services.Logging.ILogger;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks;

/// <summary>
/// Replaces the external Redash-driven "telegram-report-*" k8s CronJob + reports/telegram_daily_report.py:
/// once a day, posts a per-vendor order-total CSV (Monday-Sunday, current week) to a shared Telegram
/// chat, one message per <see cref="Company"/> in this store (a tenant DB can host several companies -
/// e.g. mysnacks hosts ServiceTitan/YourAspire/FieldRoutes - see Nop.Core.Domain.Companies.Company).
/// A single ScheduleTask/Hangfire recurring job (see
/// <see cref="Infrastructure.VendorWeeklyTelegramReportTaskMigration"/>) drives every company's report,
/// rather than one hardcoded task per company. Canonical order/vendor filter rules match
/// docs/plans/2026-06-14-redash-query-audit.md (Deleted=0, OrderStatusId&lt;&gt;40, v.Deleted=0, gift-card
/// orders included) - this intentionally changes totals slightly vs. the old Redash report, which
/// excluded gift-card orders; that was a locked decision when the canonical widgets were ported.
/// </summary>
public class VendorWeeklyTelegramReportTask : IScheduleTask
{
    private static readonly string[] DAY_NAMES =
        { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    // Telegram flood control: pace sequential sends into the same chat.
    private static readonly TimeSpan SEND_PACING = TimeSpan.FromSeconds(1.2);

    // The primary/allowance report. Every company always gets this one (even empty). Any OTHER
    // payment method that has orders in the week gets its own separate CSV - see ExecuteAsync.
    private const string MAIN_PAYMENT_METHOD = "Payments.CheckMoneyOrder";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderItem> _orderItemRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<Vendor> _vendorRepository;
    private readonly ICompanyService _companyService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IStoreContext _storeContext;
    private readonly NotificationManagerSettings _settings;
    private readonly ILogger _logger;

    public VendorWeeklyTelegramReportTask(
        IRepository<Order> orderRepository,
        IRepository<OrderItem> orderItemRepository,
        IRepository<Product> productRepository,
        IRepository<Vendor> vendorRepository,
        ICompanyService companyService,
        IDateTimeHelper dateTimeHelper,
        IStoreContext storeContext,
        NotificationManagerSettings settings,
        ILogger logger)
    {
        _orderRepository = orderRepository;
        _orderItemRepository = orderItemRepository;
        _productRepository = productRepository;
        _vendorRepository = vendorRepository;
        _companyService = companyService;
        _dateTimeHelper = dateTimeHelper;
        _storeContext = storeContext;
        _settings = settings;
        _logger = logger;
    }

    public async System.Threading.Tasks.Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.TelegramReportBotToken) || string.IsNullOrWhiteSpace(_settings.TelegramReportChatId))
        {
            await _logger.InformationAsync("Vendor weekly Telegram report: not configured for this tenant (blank bot token or chat id) - skipping.");
            return;
        }

        if (!long.TryParse(_settings.TelegramReportChatId, out var chatIdValue))
        {
            await _logger.WarningAsync($"Vendor weekly Telegram report: TelegramReportChatId '{_settings.TelegramReportChatId}' is not a valid chat id - skipping.");
            return;
        }

        var store = await _storeContext.GetCurrentStoreAsync();
        var companies = await _companyService.GetAllCompaniesAsync(storeId: store.Id);
        if (companies.Count == 0)
        {
            await _logger.InformationAsync("Vendor weekly Telegram report: no companies configured for this store - skipping.");
            return;
        }

        // Coarse pre-filter on CreatedOnUtc (a strongly-typed datetime2 column) to keep the query
        // bounded. The real delivery-date filter runs in-memory against ScheduleDate below - that
        // column is a UTC nvarchar in SQL Server (see docs/plans/2026-06-14-redash-query-audit.md),
        // so bucketing it server-side would mean provider-specific raw SQL; this codebase's
        // established convention is portable LINQ2DB queries (SQL Server/MySQL/Postgres), so the
        // date-string parsing + timezone bucketing happens here instead. One query covers every
        // company - grouped in-memory below - rather than one round trip per company.
        var createdCutoffUtc = DateTime.UtcNow.AddDays(-45);

        var query =
            from oi in _orderItemRepository.Table
            join o in _orderRepository.Table on oi.OrderId equals o.Id
            join p in _productRepository.Table on oi.ProductId equals p.Id
            join v in _vendorRepository.Table on p.VendorId equals v.Id
            where !o.Deleted && o.OrderStatusId != 40 && !v.Deleted && o.CreatedOnUtc >= createdCutoffUtc && o.CompanyId != null
            select new { CompanyId = o.CompanyId.Value, VendorName = v.Name, oi.PriceInclTax, o.ScheduleDate, o.PaymentMethodSystemName };

        var rows = await query.ToListAsync();
        var rowsByCompany = rows.ToLookup(r => r.CompanyId);

        var botClient = new TelegramBotClient(_settings.TelegramReportBotToken);
        var chatId = new ChatId(chatIdValue);
        var sentCount = 0;

        foreach (var company in companies)
        {
            var companyTimeZone = ResolveTimeZone(company.TimeZone) ?? _dateTimeHelper.DefaultStoreTimeZone;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, companyTimeZone);
            var mondayOffset = ((int)nowLocal.DayOfWeek + 6) % 7; // DayOfWeek.Monday=1 ... Sunday=0
            var weekStartLocal = nowLocal.Date.AddDays(-mondayOffset);
            var today = nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // Per payment method: vendor -> [Mon..Sun] totals. The raw PaymentMethodSystemName is the
            // bucket key so each method produces its own CSV; blank/null-method orders bucket under an
            // explicit "Unspecified" group so they can never hide inside the allowance report.
            var totalsByMethod = new Dictionary<string, SortedDictionary<string, decimal[]>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rowsByCompany[company.Id])
            {
                if (row.ScheduleDate == default)
                    continue;

                var scheduleLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.ScheduleDate, DateTimeKind.Utc), companyTimeZone);
                var dayIndex = (scheduleLocal.Date - weekStartLocal).Days;
                if (dayIndex < 0 || dayIndex > 6)
                    continue;

                var methodKey = string.IsNullOrWhiteSpace(row.PaymentMethodSystemName) ? string.Empty : row.PaymentMethodSystemName;

                if (!totalsByMethod.TryGetValue(methodKey, out var totals))
                {
                    totals = new SortedDictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase);
                    totalsByMethod[methodKey] = totals;
                }

                if (!totals.TryGetValue(row.VendorName, out var dayTotals))
                {
                    dayTotals = new decimal[7];
                    totals[row.VendorName] = dayTotals;
                }

                dayTotals[dayIndex] += row.PriceInclTax;
            }

            // 1) Main report: allowance (CheckMoneyOrder) ONLY. Always sent, even when empty, so the
            //    daily cadence accountants rely on never silently disappears.
            totalsByMethod.TryGetValue(MAIN_PAYMENT_METHOD, out var mainTotals);
            await SendMethodReportAsync(botClient, chatId, company, MAIN_PAYMENT_METHOD,
                mainTotals ?? new SortedDictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase),
                today, sentCount);
            sentCount++;

            // 2) One separate, clearly-named CSV per OTHER payment method that actually has orders.
            foreach (var method in totalsByMethod.Keys
                         .Where(k => !string.Equals(k, MAIN_PAYMENT_METHOD, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                await SendMethodReportAsync(botClient, chatId, company, method, totalsByMethod[method], today, sentCount);
                sentCount++;
            }
        }

        await _logger.InformationAsync($"Vendor weekly Telegram report: sent {sentCount} report file(s) for store '{store.Name}'.");
    }

    /// <summary>
    /// Builds and sends one payment-method CSV for a company. Filename and caption both carry the
    /// human-readable method label so accountants can never mistake an allowance report for a card
    /// (or other) report: "{Company} - {Method} - {date}.csv".
    /// </summary>
    private async System.Threading.Tasks.Task SendMethodReportAsync(
        TelegramBotClient botClient, ChatId chatId, Company company, string paymentMethodSystemName,
        SortedDictionary<string, decimal[]> totals, string today, int sentSoFar)
    {
        if (sentSoFar > 0)
            await System.Threading.Tasks.Task.Delay(SEND_PACING);

        var label = PaymentMethodLabel(paymentMethodSystemName);
        var csv = BuildCsv(totals);
        var fileName = SanitizeFileName($"{company.Name} - {label} - {today}") + ".csv";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await botClient.SendDocument(
            chatId: chatId,
            document: new InputFileStream(stream, fileName),
            caption: $"({company.Name}) {label} - daily report {today}");
    }

    /// <summary>
    /// Maps a stored PaymentMethodSystemName to an accountant-facing label. Known methods get an
    /// explicit descriptive name; anything else falls back to the system name minus its "Payments."
    /// prefix; blank/null (orders with no recorded method) becomes "Unspecified payment method".
    /// </summary>
    private static string PaymentMethodLabel(string systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName))
            return "Unspecified payment method";

        return systemName switch
        {
            "Payments.CheckMoneyOrder" => "Allowance (CheckMoneyOrder)",
            "Payments.AmeriaVPos" => "Card (AmeriaVPos)",
            _ => systemName.StartsWith("Payments.", StringComparison.OrdinalIgnoreCase)
                ? systemName.Substring("Payments.".Length)
                : systemName
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string companyTimeZone)
    {
        if (string.IsNullOrWhiteSpace(companyTimeZone))
            return null;

        try
        {
            return TZConvert.GetTimeZoneInfo(companyTimeZone);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BuildCsv(SortedDictionary<string, decimal[]> totals)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Vendor," + string.Join(",", DAY_NAMES));

        foreach (var (vendor, days) in totals)
        {
            sb.Append(CsvField(vendor));
            foreach (var amount in days)
            {
                sb.Append(',');
                sb.Append(amount.ToString("0.00", CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string CsvField(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(new[] { ',', '"', '\n' }) < 0)
            return value ?? string.Empty;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "company";

        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
