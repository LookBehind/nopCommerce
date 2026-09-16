using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Registry of Telegram chats (groups/channels) the bot has been seen in, so users can pick an
    /// automation's Telegram target by name from a dropdown instead of pasting a numeric chat id.
    /// </summary>
    public interface IInsightsTelegramChatService
    {
        bool Enabled { get; }

        /// <summary>Chats saved in the registry (most recently seen first).</summary>
        Task<IList<TelegramChat>> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Poll the bot for recent chats, upsert them into the registry, and return the full list.</summary>
        Task<IList<TelegramChat>> DiscoverAsync(CancellationToken cancellationToken = default);
    }
}
