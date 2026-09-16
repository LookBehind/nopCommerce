using Hangfire;
using Nop.Plugin.Company.Insights.Services;
using Nop.Services.Tasks;

namespace Nop.Plugin.Company.Insights.Infrastructure
{
    /// <summary>
    /// At boot: re-registers schedule-kind agents' Hangfire jobs and the daily retention cleanup
    /// (event stream + run ledger, 30-day). Resolved via engine.ResolveAll&lt;IRecurringTaskRegistrar&gt;().
    /// </summary>
    public class InsightsAgentBootRegistrar : IRecurringTaskRegistrar
    {
        private readonly IInsightsAgentConfigService _configs;
        private readonly IRecurringJobManager _recurringJobManager;

        public InsightsAgentBootRegistrar(IInsightsAgentConfigService configs, IRecurringJobManager recurringJobManager)
        {
            _configs = configs;
            _recurringJobManager = recurringJobManager;
        }

        public async System.Threading.Tasks.Task RegisterAsync()
        {
            await _configs.SeedBuiltInsAsync();
            await _configs.SyncSchedulesAsync();
            // Daily at 03:30 UTC.
            _recurringJobManager.AddOrUpdate<IInsightsRetentionJob>("insights-retention-cleanup", j => j.RunAsync(), "30 3 * * *");
        }
    }
}
