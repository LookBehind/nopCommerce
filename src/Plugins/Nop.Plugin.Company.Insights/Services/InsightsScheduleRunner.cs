using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    public interface IInsightsScheduleRunner
    {
        /// <summary>Invoked by Hangfire on the schedule's cron: run the report and post it to Telegram.</summary>
        Task RunAsync(string scheduleId);
    }

    public class InsightsScheduleRunner : IInsightsScheduleRunner
    {
        private const int MaxRows = 30;

        private readonly IInsightsScheduleService _schedules;
        private readonly IInsightsReportService _reports;
        private readonly InsightsTelegramClient _telegram;
        private readonly ILogger _logger;

        public InsightsScheduleRunner(
            IInsightsScheduleService schedules,
            IInsightsReportService reports,
            InsightsTelegramClient telegram,
            ILogger logger)
        {
            _schedules = schedules;
            _reports = reports;
            _telegram = telegram;
            _logger = logger;
        }

        public async Task RunAsync(string scheduleId)
        {
            try
            {
                var schedule = await _schedules.GetAsync(scheduleId);
                if (schedule == null || !schedule.Enabled)
                    return;

                var result = await _reports.RunAsync(schedule.ReportId, null);
                if (result == null)
                {
                    await _logger.WarningAsync($"Insights schedule '{schedule.Name}': unknown report '{schedule.ReportId}'");
                    return;
                }

                var html = FormatReport(schedule.Name, result);
                var sent = await _telegram.SendMessageAsync(schedule.TelegramChatId, html, CancellationToken.None);
                if (!sent)
                    await _logger.WarningAsync($"Insights schedule '{schedule.Name}': Telegram send failed (token/chat/config).");
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Insights schedule run failed ({scheduleId})", ex);
            }
        }

        private static string FormatReport(string name, InsightsReportResult result)
        {
            var cols = result.Columns;
            var rows = result.Rows.Take(MaxRows).ToList();

            var widths = new int[cols.Count];
            for (var c = 0; c < cols.Count; c++)
            {
                widths[c] = cols[c].Name.Length;
                foreach (var row in rows)
                    widths[c] = Math.Max(widths[c], Cell(row, cols[c].Name).Length);
            }

            var table = new StringBuilder();
            table.Append(Line(cols.Select((col, i) => col.Name.PadRight(widths[i])))).Append('\n');
            table.Append(Line(widths.Select(w => new string('-', w)))).Append('\n');
            foreach (var row in rows)
                table.Append(Line(cols.Select((col, i) => Cell(row, col.Name).PadRight(widths[i])))).Append('\n');

            var extra = result.Rows.Count > MaxRows ? $"\n… {result.Rows.Count - MaxRows} more rows" : "";
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

            return $"<b>{Escape(name)}</b> — {stamp}\n<pre>{Escape(table.ToString().TrimEnd())}{Escape(extra)}</pre>";
        }

        private static string Line(IEnumerable<string> cells) => string.Join("  ", cells);

        private static string Cell(IDictionary<string, object> row, string col)
        {
            if (!row.TryGetValue(col, out var v) || v == null)
                return "";
            return v switch
            {
                decimal d => d.ToString("0.##"),
                double db => db.ToString("0.##"),
                _ => v.ToString()
            };
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
