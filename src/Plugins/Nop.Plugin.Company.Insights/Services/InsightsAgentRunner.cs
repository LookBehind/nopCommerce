using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>Executes a background agent (Hangfire-invoked): read-only LLM task → sinks → run ledger.</summary>
    public interface IInsightsAgentRunner
    {
        Task RunForEventAsync(string agentId, long eventId);
        Task RunScheduledAsync(string agentId);
    }

    public class InsightsAgentRunner : IInsightsAgentRunner
    {
        // No token cap (a fixed limit truncates a reasoning model mid-think → empty output); bound by time.
        private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(240);

        private readonly IInsightsAgentConfigService _configs;
        private readonly IInsightsEventService _events;
        private readonly IInsightsAgentRunService _runs;
        private readonly InsightsLlmClient _llm;
        private readonly InsightsTelegramClient _telegram;
        private readonly IInsightsMemoryService _memory;
        private readonly ILogger _logger;

        public InsightsAgentRunner(
            IInsightsAgentConfigService configs,
            IInsightsEventService events,
            IInsightsAgentRunService runs,
            InsightsLlmClient llm,
            InsightsTelegramClient telegram,
            IInsightsMemoryService memory,
            ILogger logger)
        {
            _configs = configs;
            _events = events;
            _runs = runs;
            _llm = llm;
            _telegram = telegram;
            _memory = memory;
            _logger = logger;
        }

        public async Task RunForEventAsync(string agentId, long eventId)
        {
            var config = await _configs.GetAsync(agentId);
            if (config == null || !config.Enabled)
                return;

            var ev = await _events.GetAsync(eventId);
            var input = ev == null
                ? "{}"
                : JsonSerializer.Serialize(new { ev.EventType, ev.EntityType, ev.EntityId, ev.CompanyId, payload = ev.Payload });

            await RunAsync(config, eventId, ev?.EventType ?? "event", input);
        }

        public async Task RunScheduledAsync(string agentId)
        {
            var config = await _configs.GetAsync(agentId);
            if (config == null || !config.Enabled)
                return;

            var input = JsonSerializer.Serialize(new { trigger = "schedule", cron = config.Cron, config.CompanyId });
            await RunAsync(config, null, "schedule", input);
        }

        private async Task RunAsync(InsightsAgentConfig config, long? eventId, string triggerType, string inputJson)
        {
            var runId = await _runs.StartAsync(config.Id, config.Name, eventId, triggerType, inputJson);
            try
            {
                var system = (config.SystemPrompt ?? "You are a background analytics agent for the MySnacks platform. You are READ-ONLY.").Trim();
                if (config.CompanyId.HasValue)
                    system += "\nYour scope is a single company; the trigger context below is already limited to it.";

                var user = (config.Instruction ?? "Analyse the trigger and produce a concise, actionable note.").Trim()
                           + "\n\nTrigger context (JSON):\n" + inputJson;

                var messages = new List<InsightsLlmClient.LlmMessage>
                {
                    new InsightsLlmClient.LlmMessage { Role = "system", Content = system },
                    new InsightsLlmClient.LlmMessage { Role = "user", Content = user }
                };

                var output = await _llm.CompleteAsync(InsightsLlmClient.DefaultModel, messages, 0.2, null, LlmTimeout);
                output = (output ?? string.Empty).Trim();

                var sinks = ParseSinks(config.OutputSinksJson);
                await DispatchSinksAsync(config, sinks, output);

                await _runs.FinishAsync(runId, "ok",
                    JsonSerializer.Serialize(new { text = output, sinks }), null);
            }
            catch (Exception ex)
            {
                // Cold-start (model scales to zero) surfaces here as a timeout — recorded, not fatal.
                await _runs.FinishAsync(runId, "error", null, ex.Message);
                await _logger.WarningAsync($"Insights agent '{config.Name}' run failed", ex);
            }
        }

        private async Task DispatchSinksAsync(InsightsAgentConfig config, IList<string> sinks, string output)
        {
            foreach (var sink in sinks)
            {
                try
                {
                    switch ((sink ?? "").ToLowerInvariant())
                    {
                        case "telegram":
                            if (!string.IsNullOrWhiteSpace(config.OutputTarget))
                                await _telegram.SendMessageAsync(config.OutputTarget,
                                    $"<b>{WebUtility.HtmlEncode(config.Name)}</b>\n{WebUtility.HtmlEncode(output)}");
                            break;
                        case "memory":
                            await _memory.RememberAsync(config.Id, "agent-output", output);
                            break;
                        // "dashboard" / "log": always available via the run ledger output.
                    }
                }
                catch (Exception ex)
                {
                    await _logger.WarningAsync($"Insights agent '{config.Name}': sink '{sink}' failed", ex);
                }
            }
        }

        private static IList<string> ParseSinks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }
    }
}
