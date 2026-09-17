using System;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Runs one conversational turn: the LLM plans over read-only data tools (via a JSON action
    /// protocol) and may propose chart/table widgets the user can pin to the canvas.
    /// </summary>
    public interface IInsightsAgentService
    {
        /// <summary>
        /// Runs one turn under the given profile (persona) and data scope. The scope is applied to
        /// every data tool so a Workplace Manager only ever sees their company's data. Optional
        /// <paramref name="reportStatus"/> is invoked with short progress notes (e.g. "Running a report…")
        /// which the SSE endpoint streams to the client so the connection stays alive on slow turns.
        /// </summary>
        Task<AgentTurnResult> RunTurnAsync(ChatTurnRequest request, InsightsProfile profile, ReportScope scope, Action<string> reportStatus = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a background automation as a read-only tool-using agent: the config's system prompt +
        /// instruction + trigger context, with the same data tools as the chat (run_report, list_products,
        /// list_orders, list_reviews, query_orders, recall) so it grounds its output in real data. No writes,
        /// no widgets. Returns the answer text (which the runner then routes to the automation's sinks).
        /// </summary>
        Task<string> RunBackgroundAsync(InsightsAgentConfig config, string triggerJson, ReportScope scope, CancellationToken cancellationToken = default);

        /// <summary>
        /// Meta-agent: turn a natural-language description into a draft background-agent config JSON
        /// (the main agent writes the background agent's system prompt). Returns the raw JSON object
        /// string, or null if the model couldn't produce one. See docs/BACKGROUND-AGENTS.md §4.
        /// </summary>
        Task<string> DraftAgentAsync(string description, CancellationToken cancellationToken = default);
    }
}
