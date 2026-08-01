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
    /// False when the MTProto user-account session isn't configured for this tenant (the
    /// <see cref="NullTelegramGroupProvisioningService"/> fallback is registered) - every other
    /// method throws in that case, so callers (the admin config page in particular) must check this
    /// first rather than let the exception surface as an unhandled 500.
    /// </summary>
    bool IsConfigured { get; }

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

    /// <summary>
    /// Every already-mapped, real vendor group in this store that needs a topics/threads fix right
    /// now - either it's still a basic group (never forum-enabled), or a company allowed for that
    /// vendor has no forum thread yet. A single batched Telegram call covers every group's forum
    /// status; only vendors actually needing something are returned.
    /// </summary>
    Task<IReadOnlyList<VendorChatFixPreview>> GetVendorChatFixPreviewsAsync(int storeId);

    /// <summary>
    /// Upgrades an existing, real vendor group to a forum-enabled supergroup with "List" view if it
    /// isn't one already (this migration cannot be undone), then creates a forum thread for every
    /// company allowed for this vendor that doesn't have one yet. No-ops on whatever's already done -
    /// safe to call repeatedly, including via <see cref="GetVendorChatFixPreviewsAsync"/> having
    /// already flagged it.
    /// </summary>
    Task FixVendorChatTopicsAsync(int vendorId, int storeId);

    /// <summary>
    /// Runs <see cref="FixVendorChatTopicsAsync"/> for every vendor group in this store that
    /// currently needs it (best-effort per vendor - one failure doesn't stop the rest).
    /// </summary>
    Task FixAllVendorChatTopicsAsync(int storeId);

    /// <summary>
    /// Last computed result of <see cref="RefreshAutoInviteMembershipStatusAsync"/> for this store -
    /// reads an in-memory cache only, never talks to Telegram itself, so it's always fast regardless
    /// of how many groups exist. Empty (not null) if a refresh has never run in this process.
    /// </summary>
    Task<IReadOnlyList<AutoInviteMembershipStatus>> GetAutoInviteMembershipStatusAsync(int storeId);

    /// <summary>
    /// Actually checks every configured auto-invite user's current membership across every real,
    /// mapped vendor group in this store - one membership fetch per group (not per user), paced ~1.5s
    /// apart to stay under Telegram's flood-control burst limit (confirmed live: unpaced calls
    /// tripped repeated FLOOD_WAIT_30s and the resulting multi-minute request 524'd through
    /// Cloudflare). Meant to be run as a background job (see the admin controller's use of
    /// <c>IBackgroundJobClient</c>), not awaited inline in a request - caches its result for
    /// <see cref="GetAutoInviteMembershipStatusAsync"/> to read.
    /// </summary>
    Task RefreshAutoInviteMembershipStatusAsync(int storeId);

    /// <summary>
    /// Re-adds (and re-promotes to admin) an auto-invite user to every real vendor group in this
    /// store they're currently missing from - a targeted repair for exactly the gap
    /// <see cref="GetAutoInviteMembershipStatusAsync"/> found, not a full re-sweep. Same per-chat
    /// pacing as <see cref="RefreshAutoInviteMembershipStatusAsync"/> and meant to be run the same
    /// way, as a background job rather than awaited inline.
    /// </summary>
    Task FixAutoInviteUserMembershipAsync(int storeId, string identifier);
}
