using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks;

/// <summary>
/// The body of one per-slot Hangfire recurring job registered by <see cref="RateReminderReconciler"/>.
/// Fires once a day, exactly 1 hour after its slot, for the store's orders scheduled in that slot.
/// Customer-facing mirror of <see cref="PreDeliveryNudgeJob"/> (vendor Telegram, -1h before);
/// simpler body here - no vendor grouping, no Telegram, just a per-customer push. See
/// docs/plans/2026-07-29-rate-reminder-slot-jobs.md.
/// </summary>
public class RateReminderJob
{
    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly PushNotificationService _pushNotificationService;
    private readonly ILogger _logger;

    public RateReminderJob(
        IOrderService orderService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        PushNotificationService pushNotificationService,
        ILogger logger)
    {
        _orderService = orderService;
        _customerService = customerService;
        _localizationService = localizationService;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    public async Task RunForSlotAsync(int storeId, string deliveryTimeHHmm)
    {
        if (!TimeSpan.TryParse(deliveryTimeHHmm, out var slotLocal))
        {
            await _logger.ErrorAsync($"Rate reminder job: unparseable delivery time '{deliveryTimeHHmm}' for store {storeId}");
            return;
        }

        var todayUtc = DateTime.UtcNow.Date;
        var orders = await _orderService.SearchOrdersAsync(
            storeId: storeId,
            osIds: new List<int> { (int)OrderStatus.Complete },
            sendRateNotification: true,
            schedulDate: todayUtc);

        var slotOrders = orders
            .Where(o => TelegramNotificationSenderTask.GetLocalScheduleTime(o).TimeOfDay == slotLocal)
            .ToList();

        foreach (var order in slotOrders)
        {
            try
            {
                var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
                if (customer == null || !customer.RateReminderNotification)
                    continue;

                await _pushNotificationService.SendNotificationAsync(customer,
                    NotificationType.RateReminder,
                    await _localizationService.GetResourceAsync("RateRemainderNotificationTask.Title"),
                    await _localizationService.GetResourceAsync("RateRemainderNotificationTask.Body"),
                    new Dictionary<string, string> { { "Id", order.Id.ToString() } });

                var freshOrder = await _orderService.GetOrderByIdAsync(order.Id);
                freshOrder.RateNotificationSend = true;
                await _orderService.UpdateOrderAsync(freshOrder);
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Error sending rate reminder for order {order.Id}", e);
            }
        }
    }
}
