using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Vendors;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Identifies the chat ID and thread ID.
/// </summary>
/// <param name="ChatId"></param>
/// <param name="MessageThreadId">Thread ID of the chat, 0 if the group is a group or supergroup</param>
public record TelegramChatId(long ChatId, int MessageThreadId);

/// <summary>
/// Identifies a vendor and a store. Stores are used to identify the Company.
/// </summary>
/// <param name="Vendor"></param>
/// <param name="StoreId"></param>
public record VendorAssociation(Vendor Vendor, int StoreId);

/// <summary>
/// Process-wide cache mapping Telegram chats to vendors, backed by the
/// <c>VENDOR_TELEGRAM_CHANNEL_KEY</c> GenericAttribute on each Vendor (scoped per Store).
/// Shared by <see cref="Nop.Plugin.Notifications.Manager.ScheduledTasks.TelegramNotificationSenderTask"/>
/// (manual <c>/associate_with_vendor</c> flow, sending), <see cref="Nop.Plugin.Notifications.Manager.ScheduledTasks.PreDeliveryNudgeJob"/>
/// (read-only snapshot access), and <see cref="ITelegramGroupProvisioningService"/> (auto-created groups).
/// </summary>
public interface IVendorTelegramChatCache
{
    /// <summary>
    /// Current in-memory snapshot. Null until the first <see cref="EnsureLoadedAsync"/>/<see cref="ReloadAsync"/>.
    /// </summary>
    IReadOnlyDictionary<TelegramChatId, VendorAssociation> Snapshot { get; }

    /// <summary>
    /// Loads the mapping if it hasn't been loaded yet in this process.
    /// </summary>
    Task EnsureLoadedAsync();

    /// <summary>
    /// Rebuilds the mapping from the GenericAttribute store for every vendor x store.
    /// </summary>
    Task ReloadAsync();

    /// <summary>
    /// Persists a vendor+store's chat mapping and reloads the in-memory snapshot.
    /// </summary>
    Task SaveVendorChatMappingAsync(Vendor vendor, int storeId, TelegramChatId chatId);

    /// <summary>
    /// Cached group title, populated at auto-creation time or via the admin "Refresh names" action.
    /// Null for a mapping that predates title caching and hasn't been refreshed yet.
    /// </summary>
    Task<string> GetVendorChatTitleAsync(Vendor vendor, int storeId);

    /// <summary>
    /// Saves the cached group title for a vendor+store (does not touch the chat id mapping itself).
    /// </summary>
    Task SaveVendorChatTitleAsync(Vendor vendor, int storeId, string title);

    /// <summary>
    /// Telegram forum thread id for this vendor+store's Company-specific topic (created only for
    /// forum-enabled groups made from now on - see docs/plans/2026-08-01-telegram-forum-topics-per-company.md).
    /// Null if no thread has been created for this company yet.
    /// </summary>
    Task<int?> GetCompanyThreadIdAsync(Vendor vendor, int storeId, int companyId);

    /// <summary>
    /// Saves the forum thread id for a vendor+store's Company-specific topic.
    /// </summary>
    Task SaveCompanyThreadIdAsync(Vendor vendor, int storeId, int companyId, int threadId);
}
