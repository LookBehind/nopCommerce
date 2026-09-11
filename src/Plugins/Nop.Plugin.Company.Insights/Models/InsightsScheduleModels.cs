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
        public DateTime CreatedAt { get; set; }
    }
}
