using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Hangfire;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>Drains an agent-event and dispatches it to matching agents (Hangfire-invoked).</summary>
    public interface IInsightsEventDispatcher
    {
        Task DispatchAsync(long eventId);
    }

    /// <summary>
    /// Matches an event against enabled event-kind agent configs (event type + company scope + simple
    /// filters) and enqueues one agent run per match, then marks the event processed.
    /// </summary>
    public class InsightsEventDispatcher : IInsightsEventDispatcher
    {
        private readonly IInsightsEventService _events;
        private readonly IInsightsAgentConfigService _configs;
        private readonly IInsightsCompanyResolver _companies;
        private readonly ILogger _logger;

        public InsightsEventDispatcher(IInsightsEventService events, IInsightsAgentConfigService configs,
            IInsightsCompanyResolver companies, ILogger logger)
        {
            _events = events;
            _configs = configs;
            _companies = companies;
            _logger = logger;
        }

        public async Task DispatchAsync(long eventId)
        {
            try
            {
                var ev = await _events.GetAsync(eventId);
                if (ev == null || ev.Status != "new")
                    return;

                // Product/review events aren't inherently company-scoped (they carry no company_id), so a
                // company-scoped automation could never match. Resolve the owning companies from the entity's
                // vendor (fans out to all of them) so those automations fire — global automations still match.
                var companies = await ResolveCompaniesAsync(ev);
                var agents = await _configs.GetEnabledForEventAsync(ev.EventType, companies);
                foreach (var agent in agents)
                {
                    if (!PassesFilter(agent.FilterJson, ev))
                        continue;
                    try { BackgroundJob.Enqueue<IInsightsAgentRunner>(r => r.RunForEventAsync(agent.Id, ev.Id)); }
                    catch (Exception ex) { await _logger.WarningAsync($"Insights dispatch: enqueue run failed for agent {agent.Id}", ex); }
                }

                await _events.MarkProcessedAsync(eventId);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync($"Insights events: dispatch failed for event {eventId}", ex);
            }
        }

        /// <summary>Companies that own the event's entity: orders already carry one; product/review events fan
        /// out to every company mapped to the entity's vendor (payload vendorId for products, payload/entity
        /// productId for reviews). Empty means "global automations only".</summary>
        private async Task<IList<int>> ResolveCompaniesAsync(InsightsAgentEvent ev)
        {
            if (ev.CompanyId.HasValue)
                return new List<int> { ev.CompanyId.Value };
            try
            {
                int vendorId = 0, productId = 0;
                if (!string.IsNullOrWhiteSpace(ev.Payload))
                {
                    try
                    {
                        var p = JsonDocument.Parse(ev.Payload).RootElement;
                        if (p.TryGetProperty("vendorId", out var v) && v.TryGetInt32(out var vi)) vendorId = vi;
                        if (p.TryGetProperty("productId", out var pr) && pr.TryGetInt32(out var pi)) productId = pi;
                    }
                    catch { /* payload optional */ }
                }
                if (vendorId <= 0 && productId <= 0 && string.Equals(ev.EntityType, "Product", StringComparison.OrdinalIgnoreCase))
                    productId = ev.EntityId ?? 0;
                if (vendorId <= 0 && productId > 0)
                    vendorId = await _companies.VendorForProductAsync(productId);
                if (vendorId > 0)
                    return await _companies.CompaniesForVendorAsync(vendorId);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync($"Insights dispatch: company resolve failed for event {ev.Id}", ex);
            }
            return new List<int>();
        }

        /// <summary>Minimal, fail-open filter: supports {"maxRating":n} and {"vendorIds":[...]} against the payload.</summary>
        private static bool PassesFilter(string filterJson, InsightsAgentEvent ev)
        {
            if (string.IsNullOrWhiteSpace(filterJson) || filterJson.Trim() == "{}")
                return true;
            try
            {
                using var filter = JsonDocument.Parse(filterJson);
                var root = filter.RootElement;
                JsonElement payload = default;
                var hasPayload = !string.IsNullOrWhiteSpace(ev.Payload);
                if (hasPayload)
                {
                    try { payload = JsonDocument.Parse(ev.Payload).RootElement; } catch { hasPayload = false; }
                }

                if (root.TryGetProperty("maxRating", out var mr) && mr.TryGetInt32(out var maxRating))
                {
                    if (hasPayload && payload.TryGetProperty("rating", out var rt) && rt.TryGetInt32(out var rating))
                    {
                        if (rating > maxRating) return false;
                    }
                }

                if (root.TryGetProperty("vendorIds", out var vids) && vids.ValueKind == JsonValueKind.Array && vids.GetArrayLength() > 0)
                {
                    if (hasPayload && payload.TryGetProperty("vendorId", out var vEl) && vEl.TryGetInt32(out var vendorId))
                    {
                        var match = false;
                        foreach (var v in vids.EnumerateArray())
                            if (v.TryGetInt32(out var id) && id == vendorId) { match = true; break; }
                        if (!match) return false;
                    }
                }

                return true;
            }
            catch
            {
                return true; // fail-open: a bad filter shouldn't silently drop the event
            }
        }
    }
}
