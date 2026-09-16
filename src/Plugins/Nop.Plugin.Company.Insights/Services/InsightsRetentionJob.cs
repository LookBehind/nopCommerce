using System;
using System.Threading.Tasks;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>Daily retention cleanup: purge the event stream + run ledger older than 30 days.</summary>
    public interface IInsightsRetentionJob
    {
        Task RunAsync();
    }

    public class InsightsRetentionJob : IInsightsRetentionJob
    {
        private const int RetentionDays = 30;

        private readonly IInsightsEventService _events;
        private readonly IInsightsAgentRunService _runs;
        private readonly ILogger _logger;

        public InsightsRetentionJob(IInsightsEventService events, IInsightsAgentRunService runs, ILogger logger)
        {
            _events = events;
            _runs = runs;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            try
            {
                var e = await _events.PurgeOlderThanAsync(RetentionDays);
                var r = await _runs.PurgeOlderThanAsync(RetentionDays);
                if (e > 0 || r > 0)
                    await _logger.InformationAsync($"Insights retention: purged {e} events + {r} runs older than {RetentionDays} days");
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights retention cleanup failed", ex);
            }
        }
    }
}
