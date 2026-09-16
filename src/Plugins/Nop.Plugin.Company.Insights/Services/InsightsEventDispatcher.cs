using System;
using System.Threading.Tasks;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>Drains an agent-event and dispatches it to matching agents (Hangfire-invoked).</summary>
    public interface IInsightsEventDispatcher
    {
        Task DispatchAsync(long eventId);
    }

    /// <summary>
    /// Phase 1 skeleton: loads the event and marks it processed (proves the capture→stream→dispatch
    /// pipeline end to end). Later phases match enabled agent configs and enqueue agent runs.
    /// </summary>
    public class InsightsEventDispatcher : IInsightsEventDispatcher
    {
        private readonly IInsightsEventService _events;
        private readonly ILogger _logger;

        public InsightsEventDispatcher(IInsightsEventService events, ILogger logger)
        {
            _events = events;
            _logger = logger;
        }

        public async Task DispatchAsync(long eventId)
        {
            try
            {
                var ev = await _events.GetAsync(eventId);
                if (ev == null || ev.Status != "new")
                    return;

                // TODO(phase 3): match enabled insights_agent configs (event_type + scope + filters)
                // and enqueue an agent run per match. For now, just complete the pipeline.

                await _events.MarkProcessedAsync(eventId);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync($"Insights events: dispatch failed for event {eventId}", ex);
            }
        }
    }
}
