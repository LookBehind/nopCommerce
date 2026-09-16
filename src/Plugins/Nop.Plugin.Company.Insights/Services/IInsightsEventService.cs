using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// The background-agent trigger stream: a durable outbox in the separate Postgres. Producers
    /// (nopCommerce event consumers, time-based jobs) enqueue normalized events here; a Hangfire
    /// dispatcher drains them and (later) runs matching agents. Best-effort — enqueue never throws,
    /// so capturing an event can't break the storefront/admin request that produced it.
    /// See docs/BACKGROUND-AGENTS.md.
    /// </summary>
    public interface IInsightsEventService
    {
        /// <summary>True when the backing Postgres is configured.</summary>
        bool Enabled { get; }

        /// <summary>Append a trigger event and enqueue its dispatch. Returns the new row id (0 on no-op/failure).</summary>
        Task<long> EnqueueAsync(string eventType, string entityType, int? entityId, int? companyId, string payloadJson, CancellationToken cancellationToken = default);

        /// <summary>
        /// Like <see cref="EnqueueAsync"/> but skips insertion if an event of the same (event_type,
        /// entity_id) already occurred within <paramref name="dedupeWindowMinutes"/> — for idempotent
        /// time-based fires and debouncing bursty events (e.g. product-updated fires ~4x per save).
        /// Returns 0 when deduped.
        /// </summary>
        Task<long> EnqueueUniqueAsync(string eventType, string entityType, int? entityId, int? companyId, string payloadJson, int dedupeWindowMinutes, CancellationToken cancellationToken = default);

        /// <summary>Load one event by id (for the dispatcher).</summary>
        Task<InsightsAgentEvent> GetAsync(long id, CancellationToken cancellationToken = default);

        /// <summary>Mark an event terminal (processed).</summary>
        Task MarkProcessedAsync(long id, CancellationToken cancellationToken = default);

        /// <summary>Most recent events (for the observability page).</summary>
        Task<IList<InsightsAgentEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);

        /// <summary>Delete processed events older than <paramref name="days"/> (retention cleanup). Returns rows deleted.</summary>
        Task<int> PurgeOlderThanAsync(int days, CancellationToken cancellationToken = default);
    }
}
