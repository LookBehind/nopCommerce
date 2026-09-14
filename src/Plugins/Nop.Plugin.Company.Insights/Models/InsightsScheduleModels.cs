using System;

namespace Nop.Plugin.Company.Insights.Models
{
    /// <summary>A scheduled report: runs a named report on a cron and posts it to Telegram.</summary>
    public class InsightsSchedule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ReportId { get; set; }
        public string Cron { get; set; }             // standard 5-field cron (Asia/Yerevan)
        public string TelegramChatId { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>Optional report look-back window (days); null uses the report's default.</summary>
        public int? Days { get; set; }

        /// <summary>Optional row cap for the report; null uses the report's default.</summary>
        public int? Limit { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
