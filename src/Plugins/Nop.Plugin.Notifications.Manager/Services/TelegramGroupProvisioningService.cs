using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Nop.Core.Configuration;
using Nop.Core.Domain.Companies;
using Nop.Services.Companies;
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

    // Last computed auto-invite membership status per store - checking membership is a paced,
    // multi-second-per-chat Telegram sweep (see RefreshAutoInviteMembershipStatusAsync) that's too
    // slow to run inline in an HTTP request (confirmed live: it 524'd through Cloudflare), so it only
    // ever runs as a background job and writes here; GetAutoInviteMembershipStatusAsync just reads it.
    private static readonly ConcurrentDictionary<int, IReadOnlyList<AutoInviteMembershipStatus>> _membershipStatusCache = new();

    // Guards against overlapping refreshes for the same store. Confirmed live on prod (19 real
    // groups): clicking "Check group membership" a few times in a row before the first run finished
    // enqueued that many concurrent RefreshAutoInviteMembershipStatusAsync jobs, all hammering the
    // same shared lkbhnd Telegram account's rate limit at once - each run took 4+ minutes instead of
    // the ~30-45s a single paced run should take. A later click now just no-ops instead of piling on.
    private static readonly ConcurrentDictionary<int, bool> _membershipRefreshInProgress = new();

    private readonly IVendorService _vendorService;
    private readonly IStoreService _storeService;
    private readonly ICompanyService _companyService;
    private readonly IVendorTelegramChatCache _chatCache;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ISettingService _settingService;
    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;

    public TelegramGroupProvisioningService(
        IVendorService vendorService,
        IStoreService storeService,
        ICompanyService companyService,
        IVendorTelegramChatCache chatCache,
        ITelegramBotClient telegramBotClient,
        ISettingService settingService,
        ILogger logger,
        AppSettings appSettings)
    {
        _vendorService = vendorService;
        _storeService = storeService;
        _companyService = companyService;
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
    /// starts from a phone number) and leaves them as a contact on the lkbhnd account afterward -
    /// this used to delete them again immediately as "cleanup", but that bought nothing (adding
    /// someone to a group never required them to be a contact in the first place) while causing real
    /// harm: this method re-runs on every Check/Fix for every phone-based auto-invite entry, so the
    /// unconditional delete wiped out real, pre-existing contacts of the lkbhnd account whenever a
    /// phone number happened to already be a genuine contact - confirmed live, not hypothetical.
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

            return user;
        }

        var resolved = await client.Contacts_ResolveUsername(identifier.TrimStart('@'));
        return resolved.User;
    }

    /// <summary>
    /// Full channel object (access_hash + flags, incl. whether it's already forum-enabled) - not
    /// just the id+access_hash pair <see cref="ResolveInputChannelAsync"/> needs, since fixing an
    /// existing group's topics has to inspect its current forum flag first.
    /// </summary>
    private static async Task<Channel> ResolveChannelAsync(WTelegram.Client client, long chatId)
    {
        var channelId = SUPERGROUP_ID_THRESHOLD - chatId;
        var allChats = await client.Messages_GetAllChats();
        if (allChats.chats.TryGetValue(channelId, out var chatBase) && chatBase is Channel channel)
            return channel;

        throw new InvalidOperationException(
            $"Unable to resolve channel {channelId} (chat {chatId}) - the Telegram user account may not be a member of it");
    }

    private static async Task<InputChannel> ResolveInputChannelAsync(WTelegram.Client client, long chatId)
    {
        var channel = await ResolveChannelAsync(client, chatId);
        return new InputChannel(channel.id, channel.access_hash);
    }

    /// <summary>
    /// Current member user ids of a chat (basic group or supergroup) - one call per chat, reused
    /// across every auto-invite user rather than one call per (user, chat) pair. A basic group's
    /// full participant list comes back inline with Messages_GetFullChat; a supergroup needs the
    /// separate Channels_GetParticipants call (capped at 200 - real vendor groups are nowhere near
    /// that size, just the vendor's own staff + the bot + auto-invite admins).
    /// </summary>
    private static async Task<HashSet<long>> GetChatMemberUserIdsAsync(WTelegram.Client client, long chatId)
    {
        if (chatId <= SUPERGROUP_ID_THRESHOLD)
        {
            var channel = await ResolveInputChannelAsync(client, chatId);
            var result = await client.Channels_GetParticipants(channel, new ChannelParticipantsRecent(), 0, 200, 0);
            return result.participants.Select(p => p.UserId).ToHashSet();
        }

        var full = await client.Messages_GetFullChat(-chatId);
        var participants = (full.full_chat as ChatFull)?.participants?.Participants;
        return participants?.Select(p => p.UserId).ToHashSet() ?? new HashSet<long>();
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

    /// <summary>
    /// Creates a Topic in a forum-enabled supergroup and returns its thread id (the same
    /// MessageThreadId concept used everywhere else in this codebase - the Bot API's messageThreadId
    /// for a forum chat is exactly a topic's id). Reads the id back via Messages_GetForumTopics
    /// (freshest-first) rather than parsing the raw Updates from the create call, since this is
    /// always called against a brand-new group with zero prior activity - the topic just created is
    /// unambiguously the only/top result.
    /// </summary>
    private static async Task<int> CreateForumTopicAsync(WTelegram.Client client, InputPeerChannel peer, string title)
    {
        var randomId = Random.Shared.NextInt64();
        await client.Messages_CreateForumTopic(peer, title, randomId, icon_color: null, send_as: null,
            icon_emoji_id: null, title_missing: false);

        var topics = await client.Messages_GetForumTopics(peer, offset_date: default, offset_id: 0,
            offset_topic: 0, limit: 1, q: null);

        var topic = topics.topics.OfType<ForumTopic>().FirstOrDefault();
        if (topic == null)
            throw new InvalidOperationException($"Could not read back the forum topic just created ('{title}')");

        return topic.id;
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

                    autoInviteMembers.Add(new InputUser(autoInviteUser.id, autoInviteUser.access_hash));
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync(
                        $"Failed to resolve auto-invite user '{entry.DisplayName}' for new group '{title}', skipping", e);
                }
            }

            // Topics require a supergroup/channel - a plain Messages_CreateChat basic group can't
            // have them without a later migration. Channels_CreateChannel(megagroup:true, forum:true)
            // creates the group as a forum-enabled supergroup from the start, but (unlike
            // Messages_CreateChat) takes no initial-members list - the bot and auto-invite users are
            // invited afterward via the same channel-branch invite/promote logic used elsewhere.
            var created = await client.Channels_CreateChannel(title, about: null, geo_point: null,
                address: null, ttl_period: null, broadcast: false, megagroup: true, for_import: false, forum: true);

            var newChannel = created.Chats.Values.OfType<Channel>().FirstOrDefault();
            if (newChannel == null)
                throw new InvalidOperationException($"Channels_CreateChannel did not return a new Channel for vendor '{vendor.Name}'");

            var chatId = new TelegramChatId(SUPERGROUP_ID_THRESHOLD - newChannel.id, 0);
            var channelInputPeer = new InputPeerChannel(newChannel.id, newChannel.access_hash);

            await _chatCache.SaveVendorChatMappingAsync(vendor, storeId, chatId);
            await _chatCache.SaveVendorChatTitleAsync(vendor, storeId, title);

            await AddUserToChatAsync(client, chatId.ChatId, botInputUser);

            // Auto-invite users get admin rights on the groups they're added to - the bot itself
            // does not, it only ever needs to send messages.
            foreach (var inputUser in autoInviteMembers)
            {
                try
                {
                    await AddUserToChatAsync(client, chatId.ChatId, inputUser);
                    await PromoteUserToAdminAsync(client, chatId.ChatId, inputUser);
                }
                catch (Exception e) { await _logger.ErrorAsync($"Failed to add/promote auto-invite user to admin in new group '{title}'", e); }
            }

            // "List" view - Telegram's "view as messages" forum display mode (flat list) rather
            // than the default topic-tile grid.
            await client.Channels_ToggleViewForumAsMessages(new InputChannel(newChannel.id, newChannel.access_hash), enabled: true);

            // One thread per Company that has this vendor in its allowlist for this store - matches
            // the same allowlist check the admin "missing group" warning uses. New groups only; see
            // docs/plans/2026-08-01-telegram-forum-topics-per-company.md for why existing chats are
            // untouched and why this doesn't (yet) route individual notifications to these threads.
            var companies = await _companyService.GetAllCompaniesAsync(storeId: storeId, pageSize: int.MaxValue);
            foreach (var company in companies)
            {
                var companyVendors = await _companyService.GetCompanyVendorsByCompanyAsync(company.Id);
                if (!companyVendors.Any(cv => cv.VendorId == vendorId))
                    continue;

                try
                {
                    var threadId = await CreateForumTopicAsync(client, channelInputPeer, company.Name);
                    await _chatCache.SaveCompanyThreadIdAsync(vendor, storeId, company.Id, threadId);
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync($"Failed to create forum topic for company '{company.Name}' in new group '{title}'", e);
                }
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

    /// <summary>
    /// Every Company allowed for each vendor in this store, keyed by vendor id - the same allowlist
    /// check <see cref="ProvisionVendorGroupAsync"/> and the admin "missing group" warning use.
    /// </summary>
    private async Task<Dictionary<int, List<Company>>> GetVendorAllowedCompaniesAsync(int storeId)
    {
        var companies = await _companyService.GetAllCompaniesAsync(storeId: storeId, pageSize: int.MaxValue);

        var map = new Dictionary<int, List<Company>>();
        foreach (var company in companies)
        {
            var companyVendors = await _companyService.GetCompanyVendorsByCompanyAsync(company.Id);
            foreach (var companyVendor in companyVendors)
            {
                if (!map.TryGetValue(companyVendor.VendorId, out var list))
                    map[companyVendor.VendorId] = list = new List<Company>();

                list.Add(company);
            }
        }

        return map;
    }

    public async Task<IReadOnlyList<VendorChatFixPreview>> GetVendorChatFixPreviewsAsync(int storeId)
    {
        await _chatCache.EnsureLoadedAsync();
        var mappedForStore = _chatCache.Snapshot.Where(kv => kv.Value.StoreId == storeId).ToList();
        if (mappedForStore.Count == 0)
            return Array.Empty<VendorChatFixPreview>();

        var client = await GetClientAsync();

        // One batched call covers every mapped chat's current forum status - far cheaper than
        // resolving each chat individually.
        var allChats = await client.Messages_GetAllChats();
        var allowedCompaniesByVendor = await GetVendorAllowedCompaniesAsync(storeId);

        var previews = new List<VendorChatFixPreview>();
        foreach (var kv in mappedForStore)
        {
            var vendor = kv.Value.Vendor;
            var chatId = kv.Key.ChatId;
            var isSupergroup = chatId <= SUPERGROUP_ID_THRESHOLD;
            var alreadyForum = false;

            if (isSupergroup)
            {
                var channelId = SUPERGROUP_ID_THRESHOLD - chatId;
                if (allChats.chats.TryGetValue(channelId, out var chatBase) && chatBase is Channel channel)
                    alreadyForum = (channel.flags & Channel.Flags.forum) != 0;
                else
                {
                    await _logger.WarningAsync(
                        $"Could not resolve channel for vendor '{vendor.Name}' chat {chatId} while checking topics status, skipping");
                    continue;
                }
            }

            var missingCompanyNames = allowedCompaniesByVendor.TryGetValue(vendor.Id, out var allowedCompanies)
                ? new List<string>()
                : null;

            if (allowedCompanies != null)
            {
                foreach (var company in allowedCompanies)
                {
                    if (await _chatCache.GetCompanyThreadIdAsync(vendor, storeId, company.Id) == null)
                        missingCompanyNames.Add(company.Name);
                }
            }

            var needsMigration = !isSupergroup;
            if (!needsMigration && alreadyForum && (missingCompanyNames == null || missingCompanyNames.Count == 0))
                continue;

            var chatTitle = await _chatCache.GetVendorChatTitleAsync(vendor, storeId) ?? vendor.Name;
            previews.Add(new VendorChatFixPreview(vendor.Id, vendor.Name, storeId, chatTitle, chatId,
                needsMigration, alreadyForum, missingCompanyNames ?? new List<string>()));
        }

        return previews;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task FixVendorChatTopicsAsync(int vendorId, int storeId)
    {
        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (vendor == null)
        {
            await _logger.ErrorAsync($"Cannot fix Telegram group topics: vendor {vendorId} not found");
            return;
        }

        await _chatCache.EnsureLoadedAsync();
        var mappings = _chatCache.Snapshot
            .Where(kv => kv.Value.Vendor.Id == vendorId && kv.Value.StoreId == storeId)
            .ToList();
        if (mappings.Count == 0)
        {
            await _logger.ErrorAsync(
                $"Cannot fix Telegram group topics for vendor '{vendor.Name}': no chat mapping exists for store {storeId}");
            return;
        }

        var chatId = mappings[0].Key.ChatId;
        var messageThreadId = mappings[0].Key.MessageThreadId;

        try
        {
            var client = await GetClientAsync();

            if (chatId > SUPERGROUP_ID_THRESHOLD)
            {
                // Still a basic group - upgrade to a supergroup first. Telegram carries over
                // membership/history automatically and posts a migrate-from-chat-id service message
                // into the new supergroup; TelegramNotificationSenderTask.HandleMigrateFromChatId
                // independently reacts to that and re-saves the very same mapping this call is about
                // to save directly, so the two paths just agree rather than conflict.
                var migrated = await client.Messages_MigrateChat(-chatId);
                var newChannel = migrated.Chats.Values.OfType<Channel>().FirstOrDefault();
                if (newChannel == null)
                    throw new InvalidOperationException(
                        $"Messages_MigrateChat did not return a new Channel for vendor '{vendor.Name}'");

                chatId = SUPERGROUP_ID_THRESHOLD - newChannel.id;
                await _chatCache.SaveVendorChatMappingAsync(vendor, storeId, new TelegramChatId(chatId, messageThreadId));
            }

            var channel = await ResolveChannelAsync(client, chatId);
            var inputChannel = new InputChannel(channel.id, channel.access_hash);

            if ((channel.flags & Channel.Flags.forum) == 0)
            {
                await client.Channels_ToggleForum(inputChannel, enabled: true, tabs: false);
                await client.Channels_ToggleViewForumAsMessages(inputChannel, enabled: true);
            }

            var channelInputPeer = new InputPeerChannel(channel.id, channel.access_hash);
            var allowedCompaniesByVendor = await GetVendorAllowedCompaniesAsync(storeId);
            var allowedCompanies = allowedCompaniesByVendor.TryGetValue(vendorId, out var companies)
                ? companies
                : new List<Company>();

            foreach (var company in allowedCompanies)
            {
                if (await _chatCache.GetCompanyThreadIdAsync(vendor, storeId, company.Id) != null)
                    continue;

                try
                {
                    var threadId = await CreateForumTopicAsync(client, channelInputPeer, company.Name);
                    await _chatCache.SaveCompanyThreadIdAsync(vendor, storeId, company.Id, threadId);
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync(
                        $"Failed to create forum topic for company '{company.Name}' while fixing group for vendor '{vendor.Name}'", e);
                }
            }

            await _logger.InformationAsync($"Fixed Telegram group topics for vendor '{vendor.Name}' (chat {chatId})");
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync($"Error fixing Telegram group topics for vendor '{vendor.Name}'", e);
            throw;
        }
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task FixAllVendorChatTopicsAsync(int storeId)
    {
        var previews = await GetVendorChatFixPreviewsAsync(storeId);
        foreach (var preview in previews)
        {
            try
            {
                await FixVendorChatTopicsAsync(preview.VendorId, storeId);
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync(
                    $"Fix all: failed to fix Telegram group topics for vendor '{preview.VendorName}', continuing with the rest", e);
            }
        }
    }

    public Task<IReadOnlyList<AutoInviteMembershipStatus>> GetAutoInviteMembershipStatusAsync(int storeId) =>
        Task.FromResult(_membershipStatusCache.TryGetValue(storeId, out var cached)
            ? cached
            : (IReadOnlyList<AutoInviteMembershipStatus>)Array.Empty<AutoInviteMembershipStatus>());

    public bool IsAutoInviteMembershipRefreshInProgress(int storeId) =>
        _membershipRefreshInProgress.ContainsKey(storeId);

    [AutomaticRetry(Attempts = 1)]
    public async Task RefreshAutoInviteMembershipStatusAsync(int storeId)
    {
        if (!_membershipRefreshInProgress.TryAdd(storeId, true))
        {
            await _logger.InformationAsync(
                $"Skipping auto-invite membership refresh for store {storeId}: one is already in progress");
            return;
        }

        try
        {
            var entries = await GetAutoInviteEntriesInternalAsync(storeId);
            if (entries.Count == 0)
            {
                _membershipStatusCache[storeId] = Array.Empty<AutoInviteMembershipStatus>();
                return;
            }

            var client = await GetClientAsync();

            await _chatCache.EnsureLoadedAsync();
            var mappedForStore = _chatCache.Snapshot.Where(kv => kv.Value.StoreId == storeId).ToList();

            // One membership fetch per real chat, reused across every auto-invite user below - not
            // one per (user, chat) pair. Still one Channels_GetParticipants/Messages_GetFullChat call
            // per chat though, and Telegram's flood control on those methods triggers almost
            // immediately once fired back-to-back with no pacing (confirmed in prod: a burst of ~15
            // calls produced repeated FLOOD_WAIT_30 errors that WTelegramClient silently waits out and
            // retries, turning one page load into several minutes of the browser just sitting there).
            // A small delay between chats keeps this under Telegram's burst threshold instead of
            // tripping it - but only holds if a single run isn't also competing with another
            // concurrent one for the same account's rate limit, which is what the guard above prevents.
            var memberIdsByChat = new Dictionary<long, HashSet<long>>();
            var isFirstChat = true;
            foreach (var chatId in mappedForStore.Select(kv => kv.Key.ChatId).Distinct())
            {
                if (!isFirstChat)
                    await Task.Delay(1500);
                isFirstChat = false;

                try
                {
                    memberIdsByChat[chatId] = await GetChatMemberUserIdsAsync(client, chatId);
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync($"Failed to read membership for chat {chatId} while checking auto-invite status", e);
                }
            }

            var results = new List<AutoInviteMembershipStatus>();
            foreach (var entry in entries)
            {
                try
                {
                    var user = await ResolveUserAsync(client, entry.Identifier);
                    if (user == null)
                    {
                        results.Add(new AutoInviteMembershipStatus(entry.Identifier, entry.DisplayName, false, Array.Empty<MissingChatEntry>()));
                        continue;
                    }

                    var missingFrom = new List<MissingChatEntry>();
                    foreach (var kv in mappedForStore)
                    {
                        if (memberIdsByChat.TryGetValue(kv.Key.ChatId, out var members) && !members.Contains(user.id))
                        {
                            var title = await _chatCache.GetVendorChatTitleAsync(kv.Value.Vendor, storeId) ?? kv.Value.Vendor.Name;
                            missingFrom.Add(new MissingChatEntry(kv.Key.ChatId, title));
                        }
                    }

                    results.Add(new AutoInviteMembershipStatus(entry.Identifier, entry.DisplayName, true, missingFrom));
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync($"Failed to check membership for auto-invite user '{entry.DisplayName}'", e);
                }
            }

            _membershipStatusCache[storeId] = results;
        }
        finally
        {
            _membershipRefreshInProgress.TryRemove(storeId, out _);
        }
    }

    [AutomaticRetry(Attempts = 1)]
    public async Task FixAutoInviteUserMembershipAsync(int storeId, string identifier)
    {
        identifier = identifier.Trim();

        // Acts directly on the gap RefreshAutoInviteMembershipStatusAsync already found and cached -
        // no point re-checking membership for every chat again, we already know exactly which ones
        // this person is missing from. Requires a Check to have run first (the admin UI only shows a
        // Fix button once it has); if the cache is empty or stale enough that this entry isn't in it,
        // there's nothing safe to act on without a fresh Check.
        if (!_membershipStatusCache.TryGetValue(storeId, out var statuses))
        {
            await _logger.ErrorAsync(
                $"Cannot fix membership for '{identifier}': no cached membership status for store {storeId} - run 'Check group membership' first");
            return;
        }

        var status = statuses.FirstOrDefault(s => string.Equals(s.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        if (status == null || status.MissingFrom.Count == 0)
            return;

        var client = await GetClientAsync();
        var user = await ResolveUserAsync(client, identifier);
        if (user == null)
        {
            await _logger.ErrorAsync($"Could not re-resolve '{status.DisplayName}' ({identifier}) to fix their group membership");
            return;
        }

        var inputUser = new InputUser(user.id, user.access_hash);

        var fixedCount = 0;
        var isFirstChat = true;
        foreach (var missing in status.MissingFrom)
        {
            // Pacing between the add/promote calls themselves - lighter risk than the membership
            // fetches this skips entirely, but still real Telegram traffic per chat.
            if (!isFirstChat)
                await Task.Delay(1500);
            isFirstChat = false;

            try
            {
                await AddUserToChatAsync(client, missing.ChatId, inputUser);
                try { await PromoteUserToAdminAsync(client, missing.ChatId, inputUser); }
                catch (Exception e)
                {
                    await _logger.ErrorAsync(
                        $"Failed to re-promote '{status.DisplayName}' to admin after re-adding them to chat {missing.ChatId}", e);
                }

                fixedCount++;
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Failed to fix membership for '{status.DisplayName}' in chat {missing.ChatId}", e);
            }
        }

        await _logger.InformationAsync($"Fixed membership for '{status.DisplayName}' in {fixedCount} group(s)");

        // Reflect the fix immediately in the cache rather than waiting for the next full Check -
        // the entries just added shouldn't still show as "missing" until an admin re-checks.
        _membershipStatusCache[storeId] = statuses
            .Select(s => s.Identifier == status.Identifier
                ? s with { MissingFrom = Array.Empty<MissingChatEntry>() }
                : s)
            .ToList();
    }
}
