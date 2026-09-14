using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>A saved chat conversation (thread) for one backoffice user.</summary>
    public class InsightsConversation
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string AgentId { get; set; }

        /// <summary>Raw JSON array of chat messages (opaque to the server — persisted verbatim).</summary>
        public string MessagesJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Per-user persistence of the workspace (tabs/widgets/layout) and chat conversations in the
    /// separate Postgres store, keyed by tenant + the backoffice user's customer id — so a user's
    /// dashboards and history follow them across browsers/devices. Best-effort: methods degrade
    /// (return null/empty, never throw) when Postgres isn't configured, so the SPA falls back to
    /// its local copy.
    /// </summary>
    public interface IInsightsWorkspaceService
    {
        /// <summary>True when the backing Postgres is configured.</summary>
        bool Enabled { get; }

        /// <summary>The user's saved workspace JSON blob, or null if none/unavailable.</summary>
        Task<string> GetWorkspaceAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>Persist (upsert) the user's workspace JSON blob.</summary>
        Task<bool> SaveWorkspaceAsync(int userId, string dataJson, CancellationToken cancellationToken = default);

        /// <summary>Conversation headers (no message bodies) for the user, newest first.</summary>
        Task<IList<InsightsConversation>> ListConversationsAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>One full conversation (with messages), or null.</summary>
        Task<InsightsConversation> GetConversationAsync(int userId, string id, CancellationToken cancellationToken = default);

        /// <summary>Create or update a conversation; returns the stored row (with id).</summary>
        Task<InsightsConversation> SaveConversationAsync(int userId, InsightsConversation conversation, CancellationToken cancellationToken = default);

        /// <summary>Delete one conversation.</summary>
        Task DeleteConversationAsync(int userId, string id, CancellationToken cancellationToken = default);
    }
}
