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
        /// every data tool so a Workplace Manager only ever sees their company's data.
        /// </summary>
        Task<AgentTurnResult> RunTurnAsync(ChatTurnRequest request, InsightsProfile profile, ReportScope scope, CancellationToken cancellationToken = default);
    }
}
