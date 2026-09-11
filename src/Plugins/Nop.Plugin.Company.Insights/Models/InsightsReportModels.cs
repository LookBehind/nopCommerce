using System.Collections.Generic;

namespace Nop.Plugin.Company.Insights.Models
{
    /// <summary>
    /// Describes a named report the workspace can render (catalog entry).
    /// </summary>
    public class InsightsReportMeta
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>"line" | "area" | "bar" | "pie" — the chart the SPA defaults to.</summary>
        public string DefaultChart { get; set; }
        public string XField { get; set; }
        public string YField { get; set; }
        public string CategoryField { get; set; }
    }

    /// <summary>"string" | "number" | "date".</summary>
    public class InsightsReportColumn
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    /// <summary>
    /// Tabular result of running a report: column metadata + rows keyed by column name.
    /// </summary>
    public class InsightsReportResult
    {
        public string Id { get; set; }
        public IList<InsightsReportColumn> Columns { get; set; } = new List<InsightsReportColumn>();
        public IList<IDictionary<string, object>> Rows { get; set; } = new List<IDictionary<string, object>>();
    }
}
