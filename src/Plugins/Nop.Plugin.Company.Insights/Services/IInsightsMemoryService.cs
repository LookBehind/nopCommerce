using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>A recalled memory item.</summary>
    public class MemoryItem
    {
        public string Content { get; set; }
        public string Kind { get; set; }
        public double Distance { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Agent long-term memory (notetaking / learnings) in the separate Postgres + pgvector store.
    /// Persists across conversation resets. All methods are best-effort: they log and degrade
    /// (never throw) so a memory outage can't break a chat turn.
    /// </summary>
    public interface IInsightsMemoryService
    {
        bool Enabled { get; }

        /// <summary>Store a note/learning for later recall. Returns false on failure.</summary>
        Task<bool> RememberAsync(string agentId, string kind, string content, CancellationToken cancellationToken = default);

        /// <summary>Semantic recall of the most relevant stored memories. Empty on failure/disabled.</summary>
        Task<IList<MemoryItem>> RecallAsync(string agentId, string query, int k, CancellationToken cancellationToken = default);
    }
}
