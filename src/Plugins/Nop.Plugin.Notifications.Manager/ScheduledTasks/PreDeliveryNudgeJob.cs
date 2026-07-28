using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Nop.Core.Configuration;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks;

/// <summary>
/// The body of one per-slot Hangfire recurring job registered by <see cref="PreDeliveryNudgeReconciler"/>.
/// Fires once a day, exactly 1 hour before its slot. Ported and adapted from the never-merged
/// `feature/telegram-notification-overhaul` branch's `PreDeliveryReminderTask.cs` - grouping logic
/// kept, dedup key removed (unnecessary with exact-instant per-slot jobs), old "Mark delivered"
/// callback buttons replaced with the Mini App board-link button. See design §4.1.
/// </summary>
public class PreDeliveryNudgeJob
{
    private const string DELIVERED_SHORT_ADDRESS_MAP_KEY = "delivered_short_address_map_key";

    private readonly IOrderService _orderService;
    private readonly ISettingService _settingService;
    private readonly ICustomerService _customerService;
    private readonly IAddressService _addressService;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly ITelegramMiniAppAuthService _telegramMiniAppAuthService;
    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;

    public PreDeliveryNudgeJob(
        IOrderService orderService,
        ISettingService settingService,
        ICustomerService customerService,
        IAddressService addressService,
        ITelegramBotClient telegramBotClient,
        ITelegramMiniAppAuthService telegramMiniAppAuthService,
        ILogger logger,
        AppSettings appSettings)
    {
        _orderService = orderService;
        _settingService = settingService;
        _customerService = customerService;
        _addressService = addressService;
        _telegramBotClient = telegramBotClient;
        _telegramMiniAppAuthService = telegramMiniAppAuthService;
        _logger = logger;
        _appSettings = appSettings;
    }

    public async Task RunForSlotAsync(int storeId, string deliveryTimeHHmm)
    {
        if (!_appSettings.ExtendedAuthSettings.TelegramBotEnabled)
            return;

        // Belt-and-suspenders: PreDeliveryNudgeReconciler already removes this job entirely when
        // the feature is off, but a job that was already dequeued/mid-flight when the setting
        // flipped could still reach here - so guard directly too.
        var notificationManagerSettings = await _settingService.LoadSettingAsync<NotificationManagerSettings>();
        if (!notificationManagerSettings.VendorDeliveryMiniAppEnabled)
            return;

        if (!TimeSpan.TryParse(deliveryTimeHHmm, out var slotLocal))
        {
            await _logger.ErrorAsync($"Pre-delivery nudge job: unparseable delivery time '{deliveryTimeHHmm}' for store {storeId}");
            return;
        }

        var chatMappings = TelegramNotificationSenderTask.GetChatMappingsSnapshot();
        if (chatMappings == null || chatMappings.Count == 0)
            return;

        var todayUtc = DateTime.UtcNow.Date;
        var pendingStatuses = new List<int> { (int)OrderStatus.Processing, (int)OrderStatus.Pending };

        var vendorGroups = chatMappings
            .Where(kv => kv.Value.StoreId == storeId)
            .GroupBy(kv => kv.Value.Vendor.Id)
            .ToList();

        foreach (var vendorGroup in vendorGroups)
        {
            try
            {
                await SendNudgeForVendorAsync(vendorGroup.First(), storeId, slotLocal, todayUtc, pendingStatuses);
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync("Error in pre-delivery nudge job", e);
            }
        }
    }

    private async Task SendNudgeForVendorAsync(
        KeyValuePair<TelegramChatId, VendorAssociation> chatEntry,
        int storeId,
        TimeSpan slotLocal,
        DateTime todayUtc,
        List<int> pendingStatuses)
    {
        var vendor = chatEntry.Value.Vendor;
        var chatId = chatEntry.Key;

        var orders = await _orderService.SearchOrdersAsync(
            storeId: storeId,
            vendorId: vendor.Id,
            osIds: pendingStatuses,
            schedulDate: todayUtc);

        var slotOrders = orders
            .Where(o => TelegramNotificationSenderTask.GetLocalScheduleTime(o).TimeOfDay == slotLocal)
            .ToList();

        if (!slotOrders.Any())
            return;

        var shortAddressMapping = await LoadShortAddressMappingAsync(storeId);
        var locationGroups = await GroupOrdersByLocationAsync(slotOrders, vendor.Id, shortAddressMapping);

        var message = BuildNudgeMessage(slotLocal, slotOrders.Count, locationGroups);
        var boardLinkMarkup = await TelegramNotificationSenderTask.BuildBoardLinkMarkupAsync(
            _telegramBotClient, _telegramMiniAppAuthService, vendor.Id, storeId);

        await _telegramBotClient.SendMessage(
            chatId: chatId.ChatId,
            messageThreadId: chatId.MessageThreadId,
            text: message,
            parseMode: ParseMode.Html,
            replyMarkup: boardLinkMarkup);
    }

    private async Task<ShortAddressMapping> LoadShortAddressMappingAsync(int storeId)
    {
        var mappingSetting = await _settingService.GetSettingAsync(
            DELIVERED_SHORT_ADDRESS_MAP_KEY, storeId, loadSharedValueIfNotFound: true);

        if (mappingSetting == null || string.IsNullOrWhiteSpace(mappingSetting.Value))
            return null;

        return JsonSerializer.Deserialize<ShortAddressMapping>(mappingSetting.Value);
    }

    private async Task<Dictionary<string, List<(Order Order, string CustomerName, int ItemCount)>>> GroupOrdersByLocationAsync(
        List<Order> slotOrders, int vendorId, ShortAddressMapping shortAddressMapping)
    {
        var locationGroups = new Dictionary<string, List<(Order, string, int)>>();

        foreach (var order in slotOrders)
        {
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
            var customerName = customer != null
                ? await _customerService.GetCustomerFullNameAsync(customer)
                : "Unknown";

            var vendorItems = await _orderService.GetOrderItemsAsync(order.Id, vendorId: vendorId);
            var itemCount = vendorItems.Sum(i => i.Quantity);

            var locationLabel = "Unknown location";
            if (order.ShippingAddressId.HasValue)
            {
                var shippingAddress = await _addressService.GetAddressByIdAsync(order.ShippingAddressId.Value);
                locationLabel = shippingAddress?.Address1 ?? "Unknown";

                if (shortAddressMapping != null && shippingAddress != null)
                {
                    var match = shortAddressMapping.ShortAddressToDescMap
                        .FirstOrDefault(kv => string.Equals(kv.Value.Address1, shippingAddress.Address1,
                            StringComparison.OrdinalIgnoreCase));
                    if (match.Key != null)
                        locationLabel = $"{match.Key} ({shippingAddress.Address1})";
                }
            }

            if (!locationGroups.TryGetValue(locationLabel, out var list))
            {
                list = new List<(Order, string, int)>();
                locationGroups[locationLabel] = list;
            }

            list.Add((order, customerName, itemCount));
        }

        return locationGroups;
    }

    private static string BuildNudgeMessage(
        TimeSpan slotLocal, int orderCount,
        Dictionary<string, List<(Order Order, string CustomerName, int ItemCount)>> locationGroups)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>🔔 Upcoming delivery at {slotLocal:hh\\:mm}</b>");
        sb.AppendLine($"<i>{orderCount} order(s)</i>");
        sb.AppendLine();

        foreach (var (location, entries) in locationGroups)
        {
            sb.AppendLine($"📍 <b>{location}</b>");
            foreach (var (order, customerName, itemCount) in entries)
                sb.AppendLine($"  • {customerName} — #{order.CustomOrderNumber} ({itemCount} items)");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
