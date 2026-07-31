using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Nop.Core.Configuration;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Telegram.Bot;
using TL;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// See <see cref="ITelegramGroupProvisioningService"/>. The MTProto <see cref="WTelegram.Client"/> is a
/// long-lived connection authenticated as a real Telegram user account (an already-authorized session
/// file is a precondition - this class never performs the interactive phone/2FA login itself), so it's
/// kept as process-wide static state, same pattern as <see cref="VendorTelegramChatCache"/>'s shared
/// dictionary: this service stays a normal scoped class (safe to inject scoped deps like
/// <see cref="IVendorService"/>), only the underlying connection is static.
/// </summary>
public class TelegramGroupProvisioningService : ITelegramGroupProvisioningService
{
    // Bot-API chat-id convention: basic groups are the negative of their MTProto chat_id; a
    // supergroup/channel's Bot-API id is -1_000_000_000_000 minus its MTProto channel_id.
    private const long SUPERGROUP_ID_THRESHOLD = -1_000_000_000_000L;

    private static readonly SemaphoreSlim _clientInitLock = new(1, 1);
    private static WTelegram.Client _client;
    private static InputUser _cachedBotInputUser;

    private readonly IVendorService _vendorService;
    private readonly IStoreService _storeService;
    private readonly IVendorTelegramChatCache _chatCache;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ISettingService _settingService;
    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;

    public TelegramGroupProvisioningService(
        IVendorService vendorService,
        IStoreService storeService,
        IVendorTelegramChatCache chatCache,
        ITelegramBotClient telegramBotClient,
        ISettingService settingService,
        ILogger logger,
        AppSettings appSettings)
    {
        _vendorService = vendorService;
        _storeService = storeService;
        _chatCache = chatCache;
        _telegramBotClient = telegramBotClient;
        _settingService = settingService;
        _logger = logger;
        _appSettings = appSettings;
    }

    public bool IsConfigured => true;

    private async Task<WTelegram.Client> GetClientAsync()
    {
        if (_client != null)
            return _client;

        await _clientInitLock.WaitAsync();
        try
        {
            if (_client != null)
                return _client;

            var authSettings = _appSettings.ExtendedAuthSettings;

            var client = new WTelegram.Client(what => what switch
            {
                "api_id" => authSettings.TelegramUserApiId.ToString(),
                "api_hash" => authSettings.TelegramUserApiHash,
                "session_pathname" => authSettings.TelegramUserSessionPath,
                // Accept whichever user this session is already authorized as, instead of the
                // library's default phone-number-match check - we only ever expect to resume an
                // already-authorized session here, never to answer a fresh login prompt. See
                // WTelegramClient's Client.LoginUserIfNeeded source: "user_id" == "-1" short-circuits
                // its self-lookup verification without ever touching "phone_number".
                "user_id" => "-1",
                "verification_code" or "password" or "first_name" or "last_name" or "phone_number" =>
                    throw new InvalidOperationException(
                        $"Telegram user session at '{authSettings.TelegramUserSessionPath}' is missing, expired, " +
                        $"or was never authorized - the one-time interactive login must be (re-)run out-of-band; " +
                        $"the running app cannot answer a '{what}' prompt itself"),
                _ => null
            });

            try
            {
                var me = await client.LoginUserIfNeeded();
                await _logger.InformationAsync($"Telegram user-account client (vendor group auto-creation) logged in as {me} (id {me.id})");
            }
            catch
            {
                // Otherwise the session file's handle (opened inside the Client ctor) leaks and
                // permanently locks out every subsequent attempt with an unrelated IOException,
                // even after this one's root cause is fixed.
                client.Dispose();
                throw;
            }

            _client = client;
            return _client;
        }
        finally
        {
            _clientInitLock.Release();
        }
    }

    private async Task<InputUser> GetBotInputUserAsync(WTelegram.Client client)
    {
        if (_cachedBotInputUser != null)
            return _cachedBotInputUser;

        var me = await _telegramBotClient.GetMe();
        var user = await ResolveUserAsync(client, me.Username);
        if (user == null)
            throw new InvalidOperationException($"Unable to resolve bot user '@{me.Username}' via the Telegram user-account client");

        _cachedBotInputUser = new InputUser(user.id, user.access_hash);
        return _cachedBotInputUser;
    }

    /// <summary>
    /// True for anything that looks like a phone number (optional leading '+', otherwise all
    /// digits/spaces/dashes, at least 6 digits) rather than a "@username".
    /// </summary>
    private static bool LooksLikePhoneNumber(string identifier)
    {
        var digitCount = identifier.Count(char.IsDigit);
        var allowedCharCount = identifier.Count(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')');

        // Every character must be a digit or common phone punctuation (so it isn't a username),
        // and there must be enough actual digits to plausibly be a phone number. Comparing against
        // the identifier's own length (not the digit-only count) is what makes a leading '+' - or
        // spaces/dashes - not break the match.
        return digitCount >= 6 && allowedCharCount == identifier.Length;
    }

    /// <summary>
    /// Resolves a "@username" or a phone number to a Telegram <see cref="User"/> (access_hash
    /// included), or null if Telegram has no match / the target's privacy settings block discovery.
    /// Phone-number resolution goes through Contacts_ImportContacts (the only MTProto path that
    /// starts from a phone number), and the temporary contact it creates on the lkbhnd account is
    /// deleted again immediately after - this shouldn't leave every invited person as a permanent
    /// contact of the ops account.
    /// </summary>
    private static async Task<User> ResolveUserAsync(WTelegram.Client client, string identifier)
    {
        if (LooksLikePhoneNumber(identifier))
        {
            var phone = identifier.StartsWith('+') ? identifier : $"+{identifier}";
            var contact = new InputPhoneContact { client_id = 1, phone = phone, first_name = "MySnacks", last_name = "AutoInvite" };
            var imported = await client.Contacts_ImportContacts(new[] { contact });

            User user = null;
            if (imported.imported.Length > 0)
                imported.users.TryGetValue(imported.imported[0].user_id, out user);

            if (user != null)
            {
                // Best-effort cleanup - a failure here shouldn't fail the resolution itself. Uses the
                // real access_hash we just received, not a guessed/zero one.
                try { await client.Contacts_DeleteContacts(new InputUserBase[] { new InputUser(user.id, user.access_hash) }); }
                catch { /* ignore */ }
            }

            return user;
        }

        var resolved = await client.Contacts_ResolveUsername(identifier.TrimStart('@'));
        return resolved.User;
    }

    private static async Task<InputChannel> ResolveInputChannelAsync(WTelegram.Client client, long chatId)
    {
        var channelId = SUPERGROUP_ID_THRESHOLD - chatId;
        var allChats = await client.Messages_GetAllChats();
        if (allChats.chats.TryGetValue(channelId, out var chatBase) && chatBase is Channel channel)
            return new InputChannel(channel.id, channel.access_hash);

        throw new InvalidOperationException(
            $"Unable to resolve channel {channelId} (chat {chatId}) - the Telegram user account may not be a member of it");
    }

    private static async Task AddUserToChatAsync(WTelegram.Client client, long chatId, InputUser user)
    {
        if (chatId <= SUPERGROUP_ID_THRESHOLD)
        {
            var channel = await ResolveInputChannelAsync(client, chatId);
            await client.Channels_InviteToChannel(channel, new InputUserBase[] { user });
        }
        else
        {
            await client.Messages_AddChatUser(-chatId, user, fwd_limit: 0);
        }
    }

    private static async Task RemoveUserFromChatAsync(WTelegram.Client client, long chatId, InputUser user)
    {
        if (chatId <= SUPERGROUP_ID_THRESHOLD)
        {
            var channel = await ResolveInputChannelAsync(client, chatId);
            var participant = new InputPeerUser(user.user_id, user.access_hash);
            await client.Channels_EditBanned(channel, participant,
                new ChatBannedRights { flags = ChatBannedRights.Flags.view_messages });
        }
        else
        {
            await client.Messages_DeleteChatUser(-chatId, user, revoke_history: false);
        }
    }

    /// <summary>
    /// Promotes an auto-invite user to admin in a group - only ever called on add, never on
    /// removal (removal kicks them out entirely, nothing to demote). For a basic group this is an
    /// all-or-nothing flag; for a supergroup/channel it's a specific set of rights, matching what
    /// Telegram's own clients default to when you tap "promote to admin" (everything except
    /// add_admins/anonymous/manage_ranks, which aren't appropriate to hand out automatically).
    /// </summary>
    private static async Task PromoteUserToAdminAsync(WTelegram.Client client, long chatId, InputUser user)
    {
        if (chatId <= SUPERGROUP_ID_THRESHOLD)
        {
            var channel = await ResolveInputChannelAsync(client, chatId);
            var rights = new ChatAdminRights
            {
                flags = ChatAdminRights.Flags.change_info | ChatAdminRights.Flags.delete_messages |
                        ChatAdminRights.Flags.ban_users | ChatAdminRights.Flags.invite_users |
                        ChatAdminRights.Flags.pin_messages | ChatAdminRights.Flags.manage_call |
                        ChatAdminRights.Flags.manage_topics
            };
            await client.Channels_EditAdmin(channel, user, rights, rank: null);
        }
        else
        {
            await client.Messages_EditChatAdmin(-chatId, user, is_admin: true);
        }
    }

    private async Task<List<AutoInviteEntry>> GetAutoInviteEntriesInternalAsync(int storeId)
    {
        var settings = await _settingService.LoadSettingAsync<NotificationManagerSettings>(storeId);
        if (string.IsNullOrWhiteSpace(settings.AutoInviteTelegramUsersJson))
            return new List<AutoInviteEntry>();

        return JsonSerializer.Deserialize<List<AutoInviteEntry>>(settings.AutoInviteTelegramUsersJson) ?? new List<AutoInviteEntry>();
    }

    private async Task SaveAutoInviteEntriesAsync(int storeId, List<AutoInviteEntry> entries)
    {
        var settings = await _settingService.LoadSettingAsync<NotificationManagerSettings>(storeId);
        settings.AutoInviteTelegramUsersJson = JsonSerializer.Serialize(entries);
        await _settingService.SaveSettingAsync(settings, storeId);
        await _settingService.ClearCacheAsync();
    }

    private static string BuildDisplayName(User user, string fallbackIdentifier)
    {
        var name = string.Join(" ", new[] { user.first_name, user.last_name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(name))
            return user.MainUsername != null ? $"{name} (@{user.MainUsername})" : name;

        return user.MainUsername != null ? $"@{user.MainUsername}" : fallbackIdentifier;
    }

    public async Task<IReadOnlyList<AutoInviteEntry>> GetAutoInviteEntriesAsync(int storeId) =>
        await GetAutoInviteEntriesInternalAsync(storeId);

    public async Task<int> GetGroupCountAsync(int storeId)
    {
        await _chatCache.EnsureLoadedAsync();
        return _chatCache.Snapshot.Count(kv => kv.Value.StoreId == storeId);
    }

    /// <summary>
    /// Lists lkbhnd's own Telegram contacts (name + a resolvable identifier), for the admin "pick
    /// from contacts" dropdown - not a general Telegram-wide directory search, which isn't something
    /// MTProto exposes; only the account's own contact list is browsable like this.
    /// </summary>
    public async Task<IReadOnlyList<AutoInviteCandidate>> GetTelegramContactsAsync()
    {
        var client = await GetClientAsync();
        var contacts = await client.Contacts_GetContacts(0);

        return contacts.contacts
            .Select(c => contacts.users.TryGetValue(c.user_id, out var user) ? user : null)
            .Where(user => user != null)
            .Select(user =>
            {
                var identifier = user.MainUsername != null ? $"@{user.MainUsername}"
                    : !string.IsNullOrEmpty(user.phone) ? $"+{user.phone}"
                    : null;
                return (user, identifier);
            })
            .Where(x => x.identifier != null)
            .Select(x => new AutoInviteCandidate(true, x.identifier, BuildDisplayName(x.user, x.identifier), x.user.id, null))
            .OrderBy(c => c.DisplayName)
            .ToList();
    }

    public async Task<AutoInviteCandidate> ResolveAutoInviteCandidateAsync(string identifier)
    {
        identifier = identifier.Trim();

        try
        {
            var client = await GetClientAsync();
            var user = await ResolveUserAsync(client, identifier);
            if (user == null)
            {
                return new AutoInviteCandidate(false, identifier, null, 0,
                    LooksLikePhoneNumber(identifier)
                        ? "No Telegram user found for that phone number - they may have their privacy settings set to hide phone-number discovery."
                        : "No Telegram user found with that username.");
            }

            return new AutoInviteCandidate(true, identifier, BuildDisplayName(user, identifier), user.id, null);
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync($"Failed to resolve auto-invite candidate '{identifier}'", e);
            return new AutoInviteCandidate(false, identifier, null, 0, "Failed to resolve - check the logs.");
        }
    }

    public async Task<AutoInviteCandidate> AddAutoInviteUserAsync(int storeId, string identifier)
    {
        identifier = identifier.Trim();

        var client = await GetClientAsync();
        var user = await ResolveUserAsync(client, identifier);
        if (user == null)
        {
            return new AutoInviteCandidate(false, identifier, null, 0,
                LooksLikePhoneNumber(identifier)
                    ? "No Telegram user found for that phone number."
                    : "No Telegram user found with that username.");
        }

        var entries = await GetAutoInviteEntriesInternalAsync(storeId);
        if (entries.Any(e => e.TelegramUserId == user.id))
            return new AutoInviteCandidate(true, identifier, BuildDisplayName(user, identifier), user.id, null);

        var displayName = BuildDisplayName(user, identifier);
        entries.Add(new AutoInviteEntry(identifier, displayName, user.id));
        await SaveAutoInviteEntriesAsync(storeId, entries);

        var inputUser = new InputUser(user.id, user.access_hash);

        await _chatCache.EnsureLoadedAsync();
        var chatIds = _chatCache.Snapshot.Where(kv => kv.Value.StoreId == storeId)
            .Select(kv => kv.Key.ChatId).Distinct().ToList();

        foreach (var chatId in chatIds)
        {
            try
            {
                await AddUserToChatAsync(client, chatId, inputUser);

                // Admin promotion is add-only by design - removal just kicks them, no demote step.
                try { await PromoteUserToAdminAsync(client, chatId, inputUser); }
                catch (Exception e) { await _logger.ErrorAsync($"Failed to promote auto-invite user '{displayName}' to admin in chat {chatId}", e); }
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Failed to add auto-invite user '{displayName}' to chat {chatId}", e);
            }
        }

        return new AutoInviteCandidate(true, identifier, displayName, user.id, null);
    }

    public async Task RemoveAutoInviteUserAsync(int storeId, string identifier)
    {
        identifier = identifier.Trim();

        var entries = await GetAutoInviteEntriesInternalAsync(storeId);
        var match = entries.FirstOrDefault(e => string.Equals(e.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return;

        entries.Remove(match);
        await SaveAutoInviteEntriesAsync(storeId, entries);

        var client = await GetClientAsync();
        var user = await ResolveUserAsync(client, match.Identifier);
        if (user == null)
        {
            await _logger.ErrorAsync($"Could not re-resolve '{match.DisplayName}' ({match.Identifier}) to remove them from groups - removed from the list only");
            return;
        }

        var inputUser = new InputUser(user.id, user.access_hash);

        await _chatCache.EnsureLoadedAsync();
        var chatIds = _chatCache.Snapshot.Where(kv => kv.Value.StoreId == storeId)
            .Select(kv => kv.Key.ChatId).Distinct().ToList();

        foreach (var chatId in chatIds)
        {
            try
            {
                await RemoveUserFromChatAsync(client, chatId, inputUser);
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Failed to remove auto-invite user '{match.DisplayName}' from chat {chatId}", e);
            }
        }
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task ProvisionVendorGroupAsync(int vendorId, int storeId)
    {
        if (!_appSettings.ExtendedAuthSettings.TelegramBotEnabled)
            return;

        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (vendor == null)
        {
            await _logger.ErrorAsync($"Cannot auto-create Telegram group: vendor {vendorId} not found");
            return;
        }

        var store = await _storeService.GetStoreByIdAsync(storeId);
        if (store == null)
        {
            await _logger.ErrorAsync($"Cannot auto-create Telegram group: store {storeId} not found");
            return;
        }

        await _chatCache.EnsureLoadedAsync();
        if (_chatCache.Snapshot.Any(kv => kv.Value.Vendor.Id == vendorId && kv.Value.StoreId == storeId))
        {
            await _logger.InformationAsync(
                $"Vendor '{vendor.Name}' already has a Telegram chat mapping for store '{store.Name}', skipping auto-creation");
            return;
        }

        var title = $"{vendor.Name} — {store.Name}";

        try
        {
            var client = await GetClientAsync();
            var botInputUser = await GetBotInputUserAsync(client);

            var members = new List<InputUserBase> { botInputUser };
            var autoInviteMembers = new List<InputUser>();
            foreach (var entry in await GetAutoInviteEntriesInternalAsync(storeId))
            {
                try
                {
                    var autoInviteUser = await ResolveUserAsync(client, entry.Identifier);
                    if (autoInviteUser == null)
                    {
                        await _logger.ErrorAsync(
                            $"Auto-invite user '{entry.DisplayName}' no longer resolves for new group '{title}', skipping");
                        continue;
                    }

                    var inputUser = new InputUser(autoInviteUser.id, autoInviteUser.access_hash);
                    members.Add(inputUser);
                    autoInviteMembers.Add(inputUser);
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync(
                        $"Failed to resolve auto-invite user '{entry.DisplayName}' for new group '{title}', skipping", e);
                }
            }

            var invited = await client.Messages_CreateChat(members.ToArray(), title);

            var newChat = invited.updates.Chats.Values.OfType<Chat>().FirstOrDefault();
            if (newChat == null)
                throw new InvalidOperationException($"Messages_CreateChat did not return a new basic group Chat for vendor '{vendor.Name}'");

            // Basic-group Bot-API chat IDs are the negative of the MTProto Chat.ID. If Telegram later
            // auto-migrates this group to a supergroup, the existing MigrateFromChatId bot event handler
            // (TelegramNotificationSenderTask.HandleMigrateFromChatId) already re-points the mapping,
            // and Telegram itself carries existing membership across that migration automatically.
            var chatId = new TelegramChatId(-newChat.ID, 0);

            await _chatCache.SaveVendorChatMappingAsync(vendor, storeId, chatId);
            await _chatCache.SaveVendorChatTitleAsync(vendor, storeId, title);

            // Auto-invite users get admin rights on the groups they're added to - the bot itself
            // does not, it only ever needs to send messages.
            foreach (var inputUser in autoInviteMembers)
            {
                try { await PromoteUserToAdminAsync(client, chatId.ChatId, inputUser); }
                catch (Exception e) { await _logger.ErrorAsync($"Failed to promote auto-invite user to admin in new group '{title}'", e); }
            }

            await _telegramBotClient.SendMessage(chatId: chatId.ChatId,
                text: $"This group was auto-created for vendor \"{vendor.Name}\" ({store.Name}). " +
                      "Please add the vendor's own Telegram contact to this group.");

            await _logger.InformationAsync($"Auto-created Telegram group '{title}' (chat {chatId.ChatId}) for vendor '{vendor.Name}'");
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync($"Error auto-creating Telegram group '{title}' for vendor '{vendor.Name}'", e);
            throw;
        }
    }
}
