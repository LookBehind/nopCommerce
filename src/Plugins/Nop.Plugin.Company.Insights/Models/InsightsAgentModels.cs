using System.Collections.Generic;

namespace Nop.Plugin.Company.Insights.Models
{
    /// <summary>One turn in the chat history sent from the SPA.</summary>
    public class AgentChatMessage
    {
        public string Role { get; set; }   // "user" | "assistant"
        public string Content { get; set; }
    }

    /// <summary>Chat request body from the SPA.</summary>
    public class ChatTurnRequest
    {
        public string AgentId { get; set; }
        /// <summary>Active profile id (drives persona, tools, data scope).</summary>
        public string ProfileId { get; set; }
        /// <summary>Company to scope to when an admin uses a company-scoped profile (ignored for real company members).</summary>
        public int? CompanyId { get; set; }
        public List<AgentChatMessage> Messages { get; set; } = new List<AgentChatMessage>();
    }

    /// <summary>A visualization the agent proposes; the user can pin it to the canvas.</summary>
    public class WidgetProposal
    {
        public string Type { get; set; }          // "chart" | "table"
        public string Title { get; set; }
        public string ChartKind { get; set; }     // line | area | bar | pie (charts only)
        public string XField { get; set; }
        public string YField { get; set; }
        public string CategoryField { get; set; }
        public IList<InsightsReportColumn> Columns { get; set; } = new List<InsightsReportColumn>();
        public IList<IDictionary<string, object>> Rows { get; set; } = new List<IDictionary<string, object>>();
    }

    /// <summary>Result of one agent turn.</summary>
    public class AgentTurnResult
    {
        public string Reply { get; set; }
        public IList<WidgetProposal> Widgets { get; set; } = new List<WidgetProposal>();

        /// <summary>Recipes for reports the browser should compile client-side by joining other reports
        /// (raw recipe JSON, passed straight through — the backend does no data joining).</summary>
        public IList<string> CombinedReports { get; set; } = new List<string>();
    }
}
