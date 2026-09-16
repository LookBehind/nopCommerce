using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>The agent run ledger (observability). See docs/BACKGROUND-AGENTS.md §9.</summary>
    public interface IInsightsAgentRunService
    {
        bool Enabled { get; }

        /// <summary>Open a run row (status 'running'); returns its id.</summary>
        Task<long> StartAsync(string agentId, string agentName, long? eventId, string triggerType, string inputJson, CancellationToken cancellationToken = default);

        /// <summary>Close a run row with its outcome.</summary>
        Task FinishAsync(long runId, string status, string outputJson, string error, CancellationToken cancellationToken = default);

        Task<IList<InsightsAgentRun>> ListRecentAsync(int limit, string agentId, CancellationToken cancellationToken = default);

        /// <summary>Delete finished runs older than <paramref name="days"/>. Returns rows deleted.</summary>
        Task<int> PurgeOlderThanAsync(int days, CancellationToken cancellationToken = default);
    }
}
