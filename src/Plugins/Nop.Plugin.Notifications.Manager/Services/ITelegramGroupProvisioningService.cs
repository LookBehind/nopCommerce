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
    /// currently-configured auto-invite user (<see cref="GetAutoInviteEntriesAsync"/>) as an
    /// initial member of the new group, alongside the bot.
    /// </summary>
    Task ProvisionVendorGroupAsync(int vendorId, int storeId);

    /// <summary>
    /// Users currently configured to be auto-added to every vendor group in this store.
    /// </summary>
    Task<IReadOnlyList<AutoInviteEntry>> GetAutoInviteEntriesAsync(int storeId);

    /// <summary>
    /// Resolves an admin-entered identifier (a "@username", or a phone number in any of the usual
    /// written forms) against Telegram WITHOUT adding them to the auto-invite list or joining any
    /// group - lets the caller confirm "is this the right person" before the real, group-joining
    /// action. Never throws for an unresolvable identifier; check <see cref="AutoInviteCandidate.Found"/>.
    /// </summary>
    Task<AutoInviteCandidate> ResolveAutoInviteCandidateAsync(string identifier);

    /// <summary>
    /// Adds the identifier to the store's auto-invite list (re-resolving it fresh - the caller is
    /// expected to have already confirmed the match via <see cref="ResolveAutoInviteCandidateAsync"/>)
    /// and joins them to every existing vendor group in that store (best-effort per group - a failure
    /// on one group is logged and doesn't stop the others). No-ops if this person (by resolved
    /// Telegram user id) is already in the list under a different identifier.
    /// </summary>
    Task<AutoInviteCandidate> AddAutoInviteUserAsync(int storeId, string identifier);

    /// <summary>
    /// Removes the identifier from the store's auto-invite list and sweeps every vendor group in that
    /// store to remove them (best-effort per group).
    /// </summary>
    Task RemoveAutoInviteUserAsync(int storeId, string identifier);

    /// <summary>
    /// Total number of vendor chat groups mapped for this store - what a
    /// <see cref="RemoveAutoInviteUserAsync"/> call for this store will attempt to sweep. Used to
    /// show an accurate count in the admin removal-confirmation dialog.
    /// </summary>
    Task<int> GetGroupCountAsync(int storeId);

    /// <summary>
    /// Lists the MTProto account's own Telegram contacts, for an admin "pick from contacts" picker -
    /// not a general Telegram-wide directory search (MTProto doesn't expose one).
    /// </summary>
    Task<IReadOnlyList<AutoInviteCandidate>> GetTelegramContactsAsync();
}
