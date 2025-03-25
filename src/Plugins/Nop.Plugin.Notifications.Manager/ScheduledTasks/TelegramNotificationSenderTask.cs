using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Nop.Core.Configuration;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Vendors;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Message = FirebaseAdmin.Messaging.Message;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks;

public record BotEvent(string FromUsername, Chat Chat, Vendor Vendor);

public record BotAddedToGroupEvent(string FromUsername, Chat Chat, Vendor Vendor)
    : BotEvent(FromUsername, Chat, Vendor);

public record BotCommandDeliveredEvent(string FromUsername, Chat Chat, Vendor Vendor, string Command)
    : BotEvent(FromUsername, Chat, Vendor);

public class TelegramNotificationSenderTask : Services.Tasks.IScheduleTask
{
    public const string TELEGRAM_NOTIFICATION_SENDER_TASK_NAME = "Nop.Plugin.Notifications.Manager.ScheduledTasks.TelegramNotificationSenderTask";
    public const string TELEGRAM_NOTIFICATION_SENDER_FRIENDLY_NAME = "Telegram notification sender";
    private const string VENDOR_TELEGRAM_CHANNEL_KEY = nameof(VENDOR_TELEGRAM_CHANNEL_KEY);
    private const string STORE_TELEGRAM_CHANNEL_KEY = nameof(STORE_TELEGRAM_CHANNEL_KEY);
    private const string LAST_UPDATE_ID_SEEN_KEY = nameof(LAST_UPDATE_ID_SEEN_KEY);
    private static readonly string[] _trustedUsernames = { "lkbhnd", "hasmik_bars" };
    private static readonly TimeSpan _deleteEmailsOlderThan = TimeSpan.FromDays(7);

    private readonly IOrderService _orderService;
    private readonly IQueuedEmailService _queuedEmail;
    private readonly IVendorService _vendor;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IGenericAttributeService _genericAttribute;
    private readonly ISettingService _setting;
    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;
    private readonly FirebaseApp _firebaseApp;
    private readonly ICustomerService _customerService;
    private readonly IAddressService _addressService;
    private readonly IEmailAccountService _emailAccountService;

    private readonly static ConcurrentDictionary<long, Vendor> _chatIdToVendor = new();

    public TelegramNotificationSenderTask(IQueuedEmailService queuedEmail,
        IVendorService vendor,
        ITelegramBotClient telegramBotClient,
        IGenericAttributeService genericAttribute,
        ISettingService setting,
        ILogger logger,
        AppSettings appSettings, 
        IOrderService orderService,
        FirebaseApp firebaseApp, 
        ICustomerService customerService,
        IAddressService addressService,
        IEmailAccountService emailAccountService)
    {
        _queuedEmail = queuedEmail;
        _vendor = vendor;
        _telegramBotClient = telegramBotClient;
        _genericAttribute = genericAttribute;
        _setting = setting;
        _logger = logger;
        _appSettings = appSettings;
        _orderService = orderService;
        _firebaseApp = firebaseApp;
        _customerService = customerService;
        _addressService = addressService;
        _emailAccountService = emailAccountService;
    }

    private async Task<Vendor?> TryGetVendorFromChat(Chat chat)
    {
        if (_chatIdToVendor.TryGetValue(chat.Id, out var cachedVendor))
            return cachedVendor;

        var allVendors = await _vendor.GetAllVendorsAsync();
        
        foreach (var vendor in allVendors)
        {
            var channelKey = await _genericAttribute.GetAttributeAsync<long>(vendor, VENDOR_TELEGRAM_CHANNEL_KEY, 0);
            _chatIdToVendor[channelKey] = vendor;
        }

        if (chat.Title == null)
        {
            await _logger.ErrorAsync(
                $"Chat with id '{chat.Id}' doesn't have a name, please add mapping manually. {JsonSerializer.Serialize(chat)}");
            return null;
        }
        
        var vendorNameFromMessage = Regex.Match(chat.Title,
            "(.*?) orders.*",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        if (!vendorNameFromMessage.Success)
            return null;
        
        return (await _vendor.GetAllVendorsAsync(vendorNameFromMessage.Groups[1].Value)).SingleOrDefault();
    }

    private async Task HandleBotAddedToGroupEvent(BotAddedToGroupEvent botAddedToGroupEvent)
    {
        if (_trustedUsernames.Contains(botAddedToGroupEvent.FromUsername))
        {
            await _genericAttribute.SaveAttributeAsync(botAddedToGroupEvent.Vendor,
                VENDOR_TELEGRAM_CHANNEL_KEY, botAddedToGroupEvent.Chat.Id);

            await _telegramBotClient.SendTextMessageAsync(botAddedToGroupEvent.Chat,
                $"Group chat associated with vendor {botAddedToGroupEvent.Vendor.Name}");
        }
    }

    private async Task HandleBotCommandDeliveredEvent(BotCommandDeliveredEvent botCommandDeliveredEvent)
    {
        var pendingStatusOrders = new List<int>() {(int)OrderStatus.Processing, (int)OrderStatus.Pending};
        if (botCommandDeliveredEvent.Command.StartsWith("/delivered", StringComparison.OrdinalIgnoreCase))
        {           
            var orders = await _orderService.SearchOrdersAsync(
                vendorId: botCommandDeliveredEvent.Vendor.Id,
                osIds: pendingStatusOrders,
                schedulDate: DateTime.UtcNow.Date);

            if (!orders.Any())
                return;

            var qualifyingOrders = new List<Order>();
            foreach(var order in orders)
            {
                var shippingAddress = await _addressService.GetAddressByIdAsync(order.ShippingAddressId);

                var botCommandParts = botCommandDeliveredEvent.Command.Split('_');
                var scheduleHour = botCommandParts[1];
                var addressShortCode = botCommandParts[2];

                if (int.Parse(scheduleHour) == order.ScheduleDate.Hour + 4)
                {
                    if (string.Equals(addressShortCode, "melik3", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(shippingAddress.Address1, "Melik Adamyan 2/2", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(shippingAddress.Address2, "3rd floor", StringComparison.OrdinalIgnoreCase))
                            qualifyingOrders.Add(order);
                    }
                    else if (string.Equals(addressShortCode, "melik5", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(shippingAddress.Address1, "Melik Adamyan 2/2", StringComparison.OrdinalIgnoreCase) && 
                            string.Equals(shippingAddress.Address2, "5th floor", StringComparison.OrdinalIgnoreCase))
                            qualifyingOrders.Add(order);
                    }
                    else if (string.Equals(addressShortCode, "cascade", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(shippingAddress.Address1, "Cascade Antarayin 11/1", StringComparison.OrdinalIgnoreCase))
                            qualifyingOrders.Add(order);
                    }
                }
            }

            if (!qualifyingOrders.Any())
                return;

            await _logger.InformationAsync($"Found {orders.Count} orders to notify about delivery");

            foreach (var order in qualifyingOrders)
            {
                var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
                try
                {
                    order.OrderStatus = OrderStatus.Complete;
                    await _orderService.UpdateOrderAsync(order);

                    var fcmToken = await FirebaseMessaging.GetMessaging(_firebaseApp).SendAsync(new Message()
                    {
                        Token = customer.PushToken,
                        Notification = new Notification()
                        {
                            Title = "Order delivered",
                            Body = $"Your order from vendor {botCommandDeliveredEvent.Vendor.Name} has been delivered"
                        },
                        Data = new Dictionary<string, string>() { {"order_delivered", "true"} }
                    });

                    await _logger.InformationAsync($"Delivery notification token = {fcmToken}", customer: customer);
                    
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync("Failed to deliver push notification", 
                        customer: customer,
                        exception: e);
                }
            }

            await _telegramBotClient.SendTextMessageAsync(botCommandDeliveredEvent.Chat, $"Marked {qualifyingOrders.Count} as delivered");
        }
    }
    
    private async Task<IEnumerable<BotEvent>> GetUnseenBotEvents()
    {
        var events = new List<BotEvent>();
        
        try
        {
            var lastSeenUpdateId = await _setting.GetSettingByKeyAsync(LAST_UPDATE_ID_SEEN_KEY, 0);
            var updates = await _telegramBotClient.GetUpdatesAsync(
                lastSeenUpdateId, timeout: 0,
                allowedUpdates: new[] { UpdateType.Message });

            foreach (var update in updates)
            {
                try
                {
                    if (update.Type == UpdateType.Message && 
                        update.Message?.Type == MessageType.NewChatMembers &&
                        update.Message?.NewChatMembers?.Any(m => m.Id == _telegramBotClient.BotId) == true)
                    {
                        var vendor = await TryGetVendorFromChat(update.Message!.Chat);
                        if (vendor == null)
                        {
                            await _logger.ErrorAsync(
                                $"Unable to associate chat {update.Message!.Chat.Title} with any vendor");
                            continue;
                        }

                        events.Add(new BotAddedToGroupEvent(update.Message.From!.Username, update.Message.Chat, vendor));
                    }

                    if (update.Type == UpdateType.Message &&
                        update.Message!.Entities?.Any(me => me.Type == MessageEntityType.BotCommand) == true)
                    {
                        var vendor = await TryGetVendorFromChat(update.Message!.Chat);
                        if (vendor == null)
                        {
                            await _logger.ErrorAsync(
                                $"Unable to associate chat {update.Message!.Chat.Title} with any vendor");
                            continue;
                        }
                        
                        events.Add(new BotCommandDeliveredEvent(update.Message.From!.Username, 
                            update.Message.Chat, 
                            vendor, 
                            update.Message.Text!.Split('@').First()));
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

        return events;
    }

    public async Task ExecuteAsync()
    {
        if (!_appSettings.ExtendedAuthSettings.TelegramBotEnabled)
            return;

        var unseenEvents = await GetUnseenBotEvents();
        foreach (var unseenEvent in unseenEvents)
        {
            try
            {
                if (unseenEvent is BotAddedToGroupEvent botAddedToGroupEvent)
                {
                    await HandleBotAddedToGroupEvent(botAddedToGroupEvent);
                }
                else if (unseenEvent is BotCommandDeliveredEvent botCommandDeliveredEvent)
                {
                    await HandleBotCommandDeliveredEvent(botCommandDeliveredEvent);
                }
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync("Exception while handling unseen events, skipping", e);
            }
            
        }

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
                    var vendorGroupId = await _genericAttribute.GetAttributeAsync<long>(vendor,
                        VENDOR_TELEGRAM_CHANNEL_KEY, defaultValue: 0);

                    if (vendorGroupId != 0)
                    {
                        await _telegramBotClient.SendTextMessageAsync(vendorGroupId, queuedEmail.Body);
                    }
                }
                else
                {
                    var (isStoreNotification, emailAccount) = await IsNotificationforStoreAsync(queuedEmail);
                    if (isStoreNotification) {
                        var emailAccountGroupId = await _genericAttribute.GetAttributeAsync<long>(emailAccount,
                            STORE_TELEGRAM_CHANNEL_KEY, defaultValue: 0);

                        if (emailAccountGroupId != 0)
                        {
                            await _telegramBotClient.SendTextMessageAsync(emailAccountGroupId, queuedEmail.Body);
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

        await _queuedEmail.DeleteAlreadySentOrExpiredEmailsAsync(_deleteEmailsOlderThan);
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
        var matchingEmailAccount = storeEmailAccounts.FirstOrDefault(e => string.Equals(e.Email, queuedEmail.To, StringComparison.OrdinalIgnoreCase));

        return matchingEmailAccount == default ? (false, null) : (true, matchingEmailAccount);
    }
    
}