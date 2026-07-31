using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Nop.Core.Configuration;
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
    private static readonly SemaphoreSlim _clientInitLock = new(1, 1);
    private static WTelegram.Client _client;
    private static InputUser _cachedBotInputUser;

    private readonly IVendorService _vendorService;
    private readonly IStoreService _storeService;
    private readonly IVendorTelegramChatCache _chatCache;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;

    public TelegramGroupProvisioningService(
        IVendorService vendorService,
        IStoreService storeService,
        IVendorTelegramChatCache chatCache,
        ITelegramBotClient telegramBotClient,
        ILogger logger,
        AppSettings appSettings)
    {
        _vendorService = vendorService;
        _storeService = storeService;
        _chatCache = chatCache;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
        _appSettings = appSettings;
    }

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
        var resolved = await client.Contacts_ResolveUsername(me.Username);
        if (resolved.User == null)
            throw new InvalidOperationException($"Unable to resolve bot user '@{me.Username}' via the Telegram user-account client");

        _cachedBotInputUser = new InputUser(resolved.User.id, resolved.User.access_hash);
        return _cachedBotInputUser;
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

            var invited = await client.Messages_CreateChat(new InputUserBase[] { botInputUser }, title);

            var newChat = invited.updates.Chats.Values.OfType<Chat>().FirstOrDefault();
            if (newChat == null)
                throw new InvalidOperationException($"Messages_CreateChat did not return a new basic group Chat for vendor '{vendor.Name}'");

            // Basic-group Bot-API chat IDs are the negative of the MTProto Chat.ID. If Telegram later
            // auto-migrates this group to a supergroup, the existing MigrateFromChatId bot event handler
            // (TelegramNotificationSenderTask.HandleMigrateFromChatId) already re-points the mapping.
            var chatId = new TelegramChatId(-newChat.ID, 0);

            await _chatCache.SaveVendorChatMappingAsync(vendor, storeId, chatId);

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
