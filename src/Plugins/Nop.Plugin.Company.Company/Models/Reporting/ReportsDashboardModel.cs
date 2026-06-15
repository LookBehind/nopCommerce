using System;
using System.Collections.Generic;
using Nop.Plugin.Company.Company.Services.Reporting;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Models.Reporting
{
    public record ReportsDashboardModel : BaseNopModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public bool IsAdmin { get; set; }
        public IList<ReportResult> Widgets { get; set; } = new List<ReportResult>();

        /// <summary>Quick time-window presets (Kibana-style), computed in tenant-local time.</summary>
        public IList<TimePreset> Presets { get; set; } = new List<TimePreset>();

        /// <summary>Key of the preset matching the current range, or "custom".</summary>
        public string ActivePreset { get; set; } = "custom";
    }

    public record TimePreset(string Key, string Label, DateTime From, DateTime To);
}
