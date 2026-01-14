using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Logging;

namespace Nop.Plugin.Notifications.Manager.Services;

public enum NotificationType
{
    OrderStatusChange,
    RemindMe,
    RateReminder
}

public class PushNotificationService
{
    private const string FIREBASE_FAILED_COUNT = nameof(FIREBASE_FAILED_COUNT);
    private const int FIREBASE_MAX_FAILED_COUNT = 3;
    
    private readonly FirebaseMessaging _firebaseMessaging;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;

    
    public async Task SendNotificationAsync(
        int customerId,
        NotificationType notificationType,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data = null)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        await SendNotificationAsync(customer, notificationType, title, body, data);
    }
    
    public async Task SendNotificationAsync(
        Customer customer, 
        NotificationType notificationType,
        string title, 
        string body,
        IReadOnlyDictionary<string, string> data = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(customer.PushToken))
                return;

            var subscribedToNotification = notificationType switch
            {
                NotificationType.OrderStatusChange => customer.OrderStatusNotification,
                NotificationType.RemindMe => customer.RemindMeNotification,
                NotificationType.RateReminder => customer.RateReminderNotification,
                _ => throw new ArgumentOutOfRangeException(nameof(notificationType))
            };

            if (!subscribedToNotification)
                return;

            var failedCount =
                await _genericAttributeService.GetAttributeAsync<int>(customer, FIREBASE_FAILED_COUNT);
            if (failedCount >= FIREBASE_MAX_FAILED_COUNT)
            {
                customer = await _customerService.GetCustomerByIdAsync(customer.Id);
                customer.PushToken = null;
                await _customerService.UpdateCustomerAsync(customer);
                await _genericAttributeService.SaveAttributeAsync<string>(customer, FIREBASE_FAILED_COUNT, null);
                return;
            }

            await _firebaseMessaging.SendAsync(new Message()
            {
                Token = customer.PushToken,
                Notification = new Notification() { Title = title, Body = body },
                Data = data ?? new Dictionary<string, string>()
            });
        }
        catch (FirebaseMessagingException fme)
        {
            var failedCount =
                await _genericAttributeService.GetAttributeAsync<int>(customer, FIREBASE_FAILED_COUNT);
            await _genericAttributeService.SaveAttributeAsync(customer, FIREBASE_FAILED_COUNT, failedCount + 1);
            
            await _logger.ErrorAsync("Firebase threw exception",
                customer: customer,
                exception: fme);
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync("Failed to deliver push notification",
                customer: customer,
                exception: e);
        }
    }
    
    public PushNotificationService(FirebaseApp firebaseApp, ICustomerService customerService, ILogger logger, IGenericAttributeService genericAttributeService)
    {
        _customerService = customerService;
        _logger = logger;
        _genericAttributeService = genericAttributeService;
        _firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
    }
}