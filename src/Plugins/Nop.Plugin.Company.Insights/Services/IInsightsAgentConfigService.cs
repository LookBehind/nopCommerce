using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// CRUD for background-agent configs (separate Postgres) + Hangfire recurring registration for
    /// schedule-kind agents + event matching for the dispatcher. See docs/BACKGROUND-AGENTS.md §4.
    /// </summary>
    public interface IInsightsAgentConfigService
    {
        bool Enabled { get; }

        Task<IList<InsightsAgentConfig>> ListAsync(CancellationToken cancellationToken = default);
        Task<InsightsAgentConfig> GetAsync(string id, CancellationToken cancellationToken = default);
        Task<InsightsAgentConfig> UpsertAsync(InsightsAgentConfig config, CancellationToken cancellationToken = default);
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Enabled event-kind agents matching an event type + scope (company null = global agents only match null; a company event matches global + that company's agents).</summary>
        Task<IList<InsightsAgentConfig>> GetEnabledForEventAsync(string eventType, int? companyId, CancellationToken cancellationToken = default);

        /// <summary>Re-register all enabled schedule-kind agents as Hangfire recurring jobs (boot).</summary>
        Task SyncSchedulesAsync(CancellationToken cancellationToken = default);

        /// <summary>Insert the built-in agents (ready-to-enable, disabled by default) if absent. Idempotent; never clobbers user edits.</summary>
        Task SeedBuiltInsAsync(CancellationToken cancellationToken = default);
    }
}
