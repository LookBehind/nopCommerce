using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Auto-creates a Telegram group for a vendor (via an MTProto user-account client, since the Bot
/// API alone cannot create groups), adds the existing notification bot to it, and persists the
/// mapping through <see cref="IVendorTelegramChatCache"/> - the same GenericAttribute the manual
/// <c>/associate_with_vendor</c> flow writes. Also manages the store-scoped list of Telegram users
/// auto-added to every vendor group (both existing ones and future ones created by this service).
/// </summary>
public interface ITelegramGroupProvisioningService
{
    /// <summary>
    /// Idempotent: does nothing if the vendor+store already has a chat mapping. Includes every
    /// currently-configured auto-invite user (<see cref="GetAutoInviteUsernamesAsync"/>) as an
    /// initial member of the new group, alongside the bot.
    /// </summary>
    Task ProvisionVendorGroupAsync(int vendorId, int storeId);

    /// <summary>
    /// Usernames currently configured to be auto-added to every vendor group in this store.
    /// </summary>
    Task<IReadOnlyList<string>> GetAutoInviteUsernamesAsync(int storeId);

    /// <summary>
    /// Adds the username to the store's auto-invite list and joins them to every existing vendor
    /// group in that store (best-effort per group - a failure on one group is logged and doesn't
    /// stop the others).
    /// </summary>
    Task AddAutoInviteUserAsync(int storeId, string username);

    /// <summary>
    /// Removes the username from the store's auto-invite list and sweeps every vendor group in that
    /// store to remove them (best-effort per group).
    /// </summary>
    Task RemoveAutoInviteUserAsync(int storeId, string username);

    /// <summary>
    /// Total number of vendor chat groups mapped for this store - what a
    /// <see cref="RemoveAutoInviteUserAsync"/> call for this store will attempt to sweep. Used to
    /// show an accurate count in the admin removal-confirmation dialog.
    /// </summary>
    Task<int> GetGroupCountAsync(int storeId);
}
