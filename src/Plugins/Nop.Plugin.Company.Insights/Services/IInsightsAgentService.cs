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
        Task<AgentTurnResult> RunTurnAsync(ChatTurnRequest request, CancellationToken cancellationToken = default);
    }
}
