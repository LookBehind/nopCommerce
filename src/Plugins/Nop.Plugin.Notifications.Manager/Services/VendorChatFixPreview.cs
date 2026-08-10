using System.Collections.Generic;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Describes what a topics/threads "Fix" would do to one already-mapped, real, currently-active
/// vendor Telegram group - only produced for a vendor+store that actually needs something (see
/// <see cref="ITelegramGroupProvisioningService.GetVendorChatFixPreviewsAsync"/>), so its presence in
/// a result set already means "needs fixing".
/// </summary>
/// <param name="NeedsMigration">Still a basic group - fixing it upgrades it to a supergroup first
/// (Telegram carries over members/history automatically; this step cannot be undone).</param>
/// <param name="AlreadyForumEnabled">True if Topics are already on (only relevant when
/// <see cref="NeedsMigration"/> is false - a group needing migration is never forum-enabled yet).</param>
/// <param name="MissingCompanyNames">Companies that have this vendor in their allowlist for this
/// store but have no forum thread yet.</param>
public record VendorChatFixPreview(
    int VendorId,
    string VendorName,
    int StoreId,
    string ChatTitle,
    long ChatId,
    bool NeedsMigration,
    bool AlreadyForumEnabled,
    IReadOnlyList<string> MissingCompanyNames);
