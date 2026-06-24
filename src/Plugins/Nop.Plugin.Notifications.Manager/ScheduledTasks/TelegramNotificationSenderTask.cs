using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin.Messaging;
using Nop.Core.Configuration;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using IScheduledTask = Nop.Services.Tasks.IScheduleTask;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Task = System.Threading.Tasks.Task;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks;

public class ShortAddressMapping
{
    public class SimpleAddressDescription
    {
        public string Address1 { get; set; }
        public string Address2 { get; set; }
    }
    public Dictionary<string, SimpleAddressDescription> ShortAddressToDescMap { get; set; }
}

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

public class TelegramNotificationSenderTask : IScheduledTask
{
    public const string TELEGRAM_NOTIFICATION_SENDER_TASK_NAME = "Nop.Plugin.Notifications.Manager.ScheduledTasks.TelegramNotificationSenderTask";
    public const string TELEGRAM_NOTIFICATION_SENDER_FRIENDLY_NAME = "Telegram notification sender";
    private const string VENDOR_TELEGRAM_CHANNEL_KEY = nameof(VENDOR_TELEGRAM_CHANNEL_KEY);
    private const string STORE_TELEGRAM_CHANNEL_KEY = nameof(STORE_TELEGRAM_CHANNEL_KEY);
    private const string LAST_UPDATE_ID_SEEN_KEY = nameof(LAST_UPDATE_ID_SEEN_KEY);
    private const string DELIVERED_SHORT_ADDRESS_MAP_KEY = "delivered_short_address_map_key";
    private static readonly string[] _trustedUsernames = { "lkbhnd", "hasmik_bars" };
    private static readonly TimeSpan _deleteEmailsOlderThan = TimeSpan.FromDays(30);

    private readonly IOrderService _orderService;
    private readonly IQueuedEmailService _queuedEmail;
    private readonly IVendorService _vendor;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IGenericAttributeService _genericAttribute;
    private readonly ISettingService _setting;
    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;
    private readonly IAddressService _addressService;
    private readonly IEmailAccountService _emailAccountService;
    private readonly IStoreService _storeService;
    private readonly PushNotificationService _pushNotificationService;

    private static SemaphoreSlim _chatIdToVendorReloadSemaphore = new SemaphoreSlim(1, 1);
    private static Dictionary<TelegramChatId, VendorAssociation> _chatIdToVendor;

    private readonly List<(string CommandPrefix, Func<Telegram.Bot.Types.Message, Task>)> _botCommandHandlers;

    private async Task ReloadAllChatMappings()
    {
        await _chatIdToVendorReloadSemaphore.WaitAsync(10000);
        try
        {
            var newMappings = new Dictionary<TelegramChatId, VendorAssociation>();

            var allVendors = await _vendor.GetAllVendorsAsync();

            var allStores = await _storeService.GetAllStoresAsync();

            foreach (var vendor in allVendors)
            {
                foreach (var store in allStores)
                {
                    var storeChannelKey =
                        await _genericAttribute.GetAttributeAsync<string>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY,
                            store.Id);

                    if (storeChannelKey == null)
                        continue;

                    var storeChannelKeySplit = storeChannelKey?.Split(':');
                    if (storeChannelKeySplit.Length != 2)
                    {
                        await _logger.ErrorAsync(
                            $"Invalid store channel key '{storeChannelKey}' for vendor '{vendor.Name}' and store '{store.Name}'. Should be chatId:threadId");
                        continue;
                    }

                    var storeVendorChatId = new TelegramChatId(long.Parse(storeChannelKeySplit[0]),
                        int.Parse(storeChannelKeySplit[1]));

                    if (newMappings.ContainsKey(storeVendorChatId))
                        await _logger.WarningAsync(
                            $"Duplicate mapping for vendor '{vendor.Name}' and store '{store.Name}'");

                    newMappings[storeVendorChatId] = new VendorAssociation(vendor, store.Id);
                }

                // Backward compatibility
                var channelKey =
                    await _genericAttribute.GetAttributeAsync<string>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, 0);
                if (channelKey == null)
                {
                    // Already migrated
                    continue;
                }

                if (long.TryParse(channelKey, out var chatIdLong))
                {
                    // Old version (chatId only, use 0 as threadId)
                    var chatId = new TelegramChatId(chatIdLong, 0);
                    newMappings[chatId] = new VendorAssociation(vendor, allStores.First().Id);

                    await _genericAttribute.SaveAttributeAsync<string>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, null, 0);
                    await _genericAttribute.SaveAttributeAsync(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, $"{chatIdLong}:0",
                        allStores.First().Id);
                    await _logger.InformationAsync(
                        $"Updated telegram channel key for vendor '{vendor.Name}' to store-aware attribute");
                }
                else
                {
                    await _logger.WarningAsync(
                        $"Invalid telegram channel key '{channelKey}' for vendor '{vendor.Name}'. Should be long");
                }
            }

            Interlocked.Exchange(ref _chatIdToVendor, newMappings);

            await _logger.InformationAsync($"Loaded {_chatIdToVendor.Count} chat mappings");
        }
        finally
        {
            _chatIdToVendorReloadSemaphore.Release();
        }
    }
    
    private VendorAssociation TryGetVendorFromChat(Chat chat, int? messageThreadId)
    {
        messageThreadId ??= 0;
        
        if (_chatIdToVendor!.TryGetValue(new TelegramChatId(chat.Id, messageThreadId.Value), 
                out var cachedVendors))
            return cachedVendors;

        return null;
    }

    private async Task HandleBotCommandAssociateWithVendorEvent(Telegram.Bot.Types.Message botEvent)
    {
        if (_trustedUsernames.Contains(botEvent.From!.Username))
        {
            try
            {
                // /associate_with_vendor Vendor Name:Store Name
                var command = botEvent.Text!.Remove(botEvent.Entities!.First().Offset, 
                    botEvent.Entities.First().Length);
            
                var parts = command.Split(':');
                if (parts.Length != 2)
                    throw new InvalidOperationException(
                        $"Invalid command arguments for /associate_with_vendor. Expected 'Vendor Name:Store Name', got '{command}'");
            
                var vendorName = parts[0].Trim();
                var storeName = parts[1].Trim();

                var vendors = await _vendor.GetAllVendorsAsync(vendorName, showHidden: true);
                if (vendors.Count != 1)
                    throw new InvalidOperationException($"Unable to find single vendor with name '{vendorName}', " +
                                                        $"found {vendors.Count}");

                var stores = (await _storeService.GetAllStoresAsync())
                    .Where(s => string.Equals(s.Name, storeName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if(stores.Length != 1)
                    throw new InvalidOperationException($"Unable to find single store with name '{storeName}', " +
                                                        $"found {stores.Length}");
                
                var vendor = vendors.First();
                var store = stores.First();
                
                var telegramChatId = new TelegramChatId(botEvent.Chat.Id, botEvent.MessageThreadId ?? 0);
                
                await _genericAttribute.SaveAttributeAsync(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, 
                    $"{telegramChatId.ChatId}:{telegramChatId.MessageThreadId}",
                    store.Id);

                await ReloadAllChatMappings();

                await _telegramBotClient.SendMessage(chatId: botEvent.Chat,
                    messageThreadId: botEvent.MessageThreadId,
                    text: $"Group chat associated with vendor {vendor.Name} and store {store.Name}");
            }
            catch (Exception e)
            {
                await _telegramBotClient.SendMessage(chatId: botEvent.Chat,
                    messageThreadId: botEvent.MessageThreadId,
                    text: e.Message);
                await _logger.ErrorAsync("Error handling bot command associate with vendor event", e);
            }
        }
    }

    private async Task HandleMigrateFromChatId(Telegram.Bot.Types.Message botEvent)
    {
        try
        {
            foreach (var affectedVendor in 
                     _chatIdToVendor.Where(kv => kv.Key.ChatId == botEvent.MigrateFromChatId!))
            {
                var previousChannelKey = await _genericAttribute.GetAttributeAsync<string>(
                    affectedVendor.Value.Vendor,
                    VENDOR_TELEGRAM_CHANNEL_KEY, 
                    affectedVendor.Value.StoreId);
            
                var previousChannelKeySplit = previousChannelKey?.Split(':');
                if (previousChannelKeySplit == null || previousChannelKeySplit.Length != 2)
                {
                    throw new InvalidOperationException($"Invalid previous channel key format: {previousChannelKey}. Should be chatId:messageThreadId");
                }
                
                var newChannelKey = $"{botEvent.Chat.Id}:{previousChannelKeySplit[1]}";
                
                await _genericAttribute.SaveAttributeAsync(affectedVendor.Value.Vendor, 
                    VENDOR_TELEGRAM_CHANNEL_KEY, 
                    newChannelKey, 
                    affectedVendor.Value.StoreId);

                await _telegramBotClient.SendMessage(chatId: botEvent.Chat.Id,
                    messageThreadId: botEvent.MessageThreadId,
                    text:
                    $"Successfully migrated channel key for vendor {affectedVendor.Value.Vendor.Name} from chat {botEvent.MigrateFromChatId} to chat {botEvent.Chat.Id}");
            }

            await ReloadAllChatMappings();
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync("Error handling migrate from chat id event", e);
            
            await _telegramBotClient.SendMessage(chatId: botEvent.Chat,
                messageThreadId: botEvent.MessageThreadId,
                text: e.Message);
        }
        
    }
    
    private async Task HandleBotCommandDeliveredEvent(Telegram.Bot.Types.Message botEvent)
    {
        var pendingStatusOrders = new List<int>() {(int)OrderStatus.Processing, (int)OrderStatus.Pending};

        try
        {
            var vendorAssociation = TryGetVendorFromChat(botEvent.Chat, botEvent.MessageThreadId);
            if (vendorAssociation == null)
                throw new InvalidOperationException(
                    $"Unable to resolve vendor from chat {botEvent.Chat.Title} (ID: {botEvent.Chat.Id}, message thread id: {botEvent.MessageThreadId})");

            var orders = await _orderService.SearchOrdersAsync(
                storeId: vendorAssociation.StoreId,
                vendorId: vendorAssociation.Vendor.Id,
                osIds: pendingStatusOrders,
                schedulDate: DateTime.UtcNow.Date);

            if (!orders.Any())
            {
                await _telegramBotClient.SendMessage(chatId: botEvent.Chat,
                    messageThreadId: botEvent.MessageThreadId,
                    text: $"No pending orders found for today");
                return;
            }

            var mappingString = await _setting.GetSettingAsync(
                DELIVERED_SHORT_ADDRESS_MAP_KEY,
                vendorAssociation.StoreId,
                loadSharedValueIfNotFound: true);

            if (mappingString == null || string.IsNullOrWhiteSpace(mappingString.Value))
                throw new InvalidOperationException("No mapping found for short addresses, skipping");

            var shortAddressCodeMapping = JsonSerializer.Deserialize<ShortAddressMapping>(mappingString.Value);
            if (shortAddressCodeMapping == null)
                throw new InvalidOperationException("Invalid mapping for short addresses, skipping");

            // /delivered_13_melik3@mysnacks_notifier_test_bot
            // /delivered_13_melik3
            var botCommand = botEvent.EntityValues?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(botCommand))
                throw new InvalidOperationException(
                    $"Invalid bot command (EntityValues is empty), expected format " +
                    $"/delivered_<hour>_<short_address_code>@<bot_username> but got {botEvent.Text}");

            var botCommandParts = botCommand.Split('@')[0].Split('_');
            if (botCommandParts.Length != 3)
                throw new InvalidOperationException($"Invalid bot command format, expected format " +
                                                    $"/delivered_<hour>_<short_address_code>@<bot_username> but got {botEvent.Text}");

            if (!int.TryParse(botCommandParts[1], out var scheduleHour))
                throw new InvalidOperationException($"Invalid bot command format, integer hour but got {botEvent.Text}");

            var addressShortCode = botCommandParts[2];
            if (!shortAddressCodeMapping.ShortAddressToDescMap.TryGetValue(addressShortCode,
                    out var simpleAddressDescription))
                throw new InvalidOperationException(
                    $"Couldn't find mapping for short code {addressShortCode}, map is '{mappingString.Value}', skipping");
            
            var qualifyingOrders = new List<Order>();
            foreach (var order in orders)
            {
                var shippingAddress = await _addressService.GetAddressByIdAsync(order.ShippingAddressId);

                if (scheduleHour == order.ScheduleDate.Hour + 4)
                {
                    if (string.Equals(simpleAddressDescription.Address1, shippingAddress.Address1,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(simpleAddressDescription.Address2, shippingAddress.Address2,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        qualifyingOrders.Add(order);
                    }
                }
            }

            if (!qualifyingOrders.Any())
                return;

            await _logger.InformationAsync($"Found {orders.Count} orders to notify about delivery");

            foreach (var order in qualifyingOrders)
            {
                order.OrderStatus = OrderStatus.Complete;
                await _orderService.UpdateOrderAsync(order);

                await _pushNotificationService.SendNotificationAsync(order.CustomerId,
                    NotificationType.OrderStatusChange,
                    "Order delivered",
                    $"Your order from vendor {vendorAssociation.Vendor.Name} has been delivered",
                    new Dictionary<string, string>()
                    {
                        { "order_delivered", "true" },
                        { "orderId", order.Id.ToString() },
                        { "url", $"Order/{order.Id}" }
                    });
            }

            await _telegramBotClient.SendMessage(chatId: botEvent.Chat,
                messageThreadId: botEvent.MessageThreadId,
                text: $"Marked {qualifyingOrders.Count} as delivered");
        }
        catch (Exception e)
        {
            await _telegramBotClient.SendMessage(botEvent.Chat, "Error, please contact MySnacks support");
            await _logger.ErrorAsync("Error handling bot command delivered event", e);
        }
    }
    
    private async Task HandleBotEvents()
    {
        try
        {
            var lastSeenUpdateId = await _setting.GetSettingByKeyAsync(LAST_UPDATE_ID_SEEN_KEY, 0);
            
            var updates = await _telegramBotClient.GetUpdates(
                lastSeenUpdateId, 
                timeout: 0,
                allowedUpdates: new[] { UpdateType.Message });

            
            
            foreach (var update in updates)
            {
                try
                {
                    if (update.Type == UpdateType.Message &&
                        update.Message!.Entities?.Any(me => me.Type == MessageEntityType.BotCommand) == true)
                    {
                        var commandHandler = _botCommandHandlers.FirstOrDefault(p =>
                            update.Message.Text?.StartsWith(p.Item1, StringComparison.OrdinalIgnoreCase) == true);
                        
                        if(commandHandler.Item2 != null)
                        {
                            await commandHandler.Item2.Invoke(update.Message);
                        }
                        else
                        {
                            await _logger.WarningAsync($"No handler found for bot command {update.Message.Text}");
                        }
                    }
                    else if (update.Type == UpdateType.Message &&
                             update.Message!.Type == MessageType.MigrateFromChatId)
                    {
                        await HandleMigrateFromChatId(update.Message);
                    }

                    lastSeenUpdateId = Math.Max(lastSeenUpdateId, update.Id + 1);
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync("Exception while handling telegram update, skipping", e);
                }
            }

            await _setting.SetSettingAsync(LAST_UPDATE_ID_SEEN_KEY, lastSeenUpdateId);
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync("Exception while getting telegram updates, skipping", e);
        }
    }

    public async Task ExecuteAsync()
    {
        if (!_appSettings.ExtendedAuthSettings.TelegramBotEnabled)
            return;

        if(_chatIdToVendor == null)
            await ReloadAllChatMappings();
        
        await HandleBotEvents();

        var maxTries = 3;

        var queuedEmails = await _queuedEmail.SearchEmailsAsync(null, 
            null,
            null, 
            null, 
            true, 
            true, 
            maxTries,
            false);
        foreach (var queuedEmail in queuedEmails)
        {
            var beingThrottled = false;
            try
            {
                var (isVendorNotification, vendor) = await IsNotificationForVendorAsync(queuedEmail);
                if (isVendorNotification)
                {
                    var vendorChat =
                        _chatIdToVendor!.FirstOrDefault(kv => kv.Value.StoreId == queuedEmail.StoreId &&
                                                              kv.Value.Vendor.Id == vendor.Id);
                    
                    if (vendorChat.Key != null)
                    {
                        await _telegramBotClient.SendMessage(chatId: vendorChat.Key.ChatId,
                            messageThreadId: vendorChat.Key.MessageThreadId,
                            text: queuedEmail.Body);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Telegram notification wasn't delivered due to missing vendor chat. queuedEmail.Id={queuedEmail.Id}, vendor.Name={vendor.Name}");
                    }
                }
                else
                {
                    var (isStoreNotification, emailAccount) = await IsNotificationforStoreAsync(queuedEmail);
                    if (isStoreNotification) {
                        var emailAccountGroupId = await _genericAttribute.GetAttributeAsync<long>(
                            emailAccount,
                            STORE_TELEGRAM_CHANNEL_KEY, 
                            defaultValue: 0);

                        if (emailAccountGroupId != 0)
                        {
                            await _telegramBotClient.SendMessage(emailAccountGroupId, queuedEmail.Body);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Telegram notification wasn't delivered due to missing store channel key. queuedEmail.Id={queuedEmail.Id}, emailAccount.Email={emailAccount.Email}");
                        }
                    }
                }

                queuedEmail.SentOnUtc = DateTime.UtcNow;
            }
            catch (HttpRequestException exc) when (exc.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await _logger.ErrorAsync($"Telegram is throttling, ending task", exc);
                beingThrottled = true;
                return;
            }
            catch (Exception exc)
            {
                await _logger.ErrorAsync($"Error sending telegram notification", exc);
            }
            finally
            {
                if (!beingThrottled)
                {
                    queuedEmail.SentTries += 1;
                    await _queuedEmail.UpdateQueuedEmailAsync(queuedEmail);
                }
            }
        }

        await _queuedEmail.DeleteAlreadySentEmailsAsync(
            createdFromUtc: null,
            createdToUtc: DateTime.UtcNow.Subtract(_deleteEmailsOlderThan));
    }

    private async Task<(bool, Vendor)> IsNotificationForVendorAsync(QueuedEmail queuedEmail)
    {
        // TODO: use memory cache (IDistributedMemoryCache, or MemoryCache)
        var foundVendor = (await _vendor.GetAllVendorsAsync(email: queuedEmail.To)).SingleOrDefault();
        return foundVendor != null ? (true, foundVendor) : (false, null);
    }

    private async Task<(bool, EmailAccount?)> IsNotificationforStoreAsync(QueuedEmail queuedEmail)
    {
        // TODO: use memory cache (IDistributedMemoryCache, or MemoryCache)
        var storeEmailAccounts = await _emailAccountService.GetAllEmailAccountsAsync();
        var matchingEmailAccount = storeEmailAccounts.FirstOrDefault(e =>
            string.Equals(e.Email, queuedEmail.To, StringComparison.OrdinalIgnoreCase));

        return matchingEmailAccount == null ? (false, null) : (true, matchingEmailAccount);
    }
    
    public TelegramNotificationSenderTask(
        IQueuedEmailService queuedEmail,
        IVendorService vendor,
        ITelegramBotClient telegramBotClient,
        IGenericAttributeService genericAttribute,
        ISettingService setting,
        ILogger logger,
        AppSettings appSettings, 
        IOrderService orderService,
        IAddressService addressService,
        IEmailAccountService emailAccountService, 
        IStoreService storeService, 
        PushNotificationService pushNotificationService)
    {
        _queuedEmail = queuedEmail;
        _vendor = vendor;
        _telegramBotClient = telegramBotClient;
        _genericAttribute = genericAttribute;
        _setting = setting;
        _logger = logger;
        _appSettings = appSettings;
        _orderService = orderService;
        _addressService = addressService;
        _emailAccountService = emailAccountService;
        _storeService = storeService;
        _pushNotificationService = pushNotificationService;

        _botCommandHandlers = new()
        {
            new("/delivered", HandleBotCommandDeliveredEvent),
            new("/associate_with_vendor", HandleBotCommandAssociateWithVendorEvent)
        };
    }
}