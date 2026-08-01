using System.Collections.Generic;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// A confirmed auto-invite user, as stored in <c>NotificationManagerSettings.AutoInviteTelegramUsersJson</c>.
/// </summary>
/// <param name="Identifier">What the admin typed - a "@username" or a phone number. Re-resolved
/// (not cached) whenever an actual Telegram API call needs a fresh access_hash.</param>
/// <param name="DisplayName">Resolved name shown in the admin grid, captured at add time.</param>
/// <param name="TelegramUserId">Resolved numeric Telegram user id, used to de-duplicate entries
/// added via different identifiers (e.g. once by username, once by phone) for the same person.</param>
public record AutoInviteEntry(string Identifier, string DisplayName, long TelegramUserId);

/// <summary>
/// Result of resolving an admin-entered identifier (username or phone number) against Telegram,
/// without yet adding them to the auto-invite list or joining any group - lets the admin UI show
/// "this is who we found" before committing to the real, group-joining action.
/// </summary>
public record AutoInviteCandidate(bool Found, string Identifier, string DisplayName, long TelegramUserId, string Error);

/// <summary>
/// A real, mapped vendor group this person is currently missing from - both the id (for
/// <see cref="ITelegramGroupProvisioningService.FixAutoInviteUserMembershipAsync"/> to act on
/// directly, without re-checking membership from scratch) and the title (for display).
/// </summary>
public record MissingChatEntry(long ChatId, string ChatTitle);

/// <summary>
/// Whether a configured auto-invite user is actually a current member of every real, mapped vendor
/// group in a store right now - catches drift (e.g. someone kicked by mistake, like the 2026-08-01
/// incident where an unrelated cleanup accidentally swept 2 real people out of real vendor groups)
/// that the stored auto-invite list alone can't reveal.
/// </summary>
/// <param name="Found">False if the identifier no longer resolves to a Telegram user at all - a
/// distinct, worse case than just being missing from some groups.</param>
/// <param name="MissingFrom">Real vendor groups this person is not currently a member of, empty if
/// they're in every group.</param>
public record AutoInviteMembershipStatus(string Identifier, string DisplayName, bool Found, IReadOnlyList<MissingChatEntry> MissingFrom);
