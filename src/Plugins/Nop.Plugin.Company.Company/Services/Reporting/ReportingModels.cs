using System.Collections.Generic;

namespace Nop.Plugin.Company.Company.Services.Reporting
{
    /// <summary>Visualisation hint for a widget (the storefront/JS decides how to render).</summary>
    public enum WidgetViz
    {
        Metric,
        Line,
        Bar,
        Table
    }

    /// <summary>What date inputs a widget accepts.</summary>
    public enum WidgetParams
    {
        None,        // self-contained (e.g. "this week")
        DateRange    // from + to (local UTC+4 dates)
    }

    /// <summary>Metadata for a widget — drives the "list widgets" API + the dashboard layout.</summary>
    public class WidgetDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public WidgetViz Viz { get; set; }
        public WidgetParams Params { get; set; }
        public bool AdminOnly { get; set; }
    }

    /// <summary>
    /// Uniform result shape so the API can serve ANY widget the same way and the UI can
    /// render generically. Columns are ordered; each row is values aligned to Columns.
    /// </summary>
    public class ReportResult
    {
        public string WidgetId { get; set; }
        public string Title { get; set; }
        public WidgetViz Viz { get; set; }
        public IList<string> Columns { get; set; } = new List<string>();
        public IList<object[]> Rows { get; set; } = new List<object[]>();
    }
}
