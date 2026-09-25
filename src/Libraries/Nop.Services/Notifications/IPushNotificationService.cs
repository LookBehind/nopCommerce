using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;

namespace Nop.Services.Notifications
{
    public enum NotificationType
    {
        OrderStatusChange,
        RemindMe,
        RateReminder,
        SupportCaseUpdate
    }

    /// <summary>
    /// Lets any plugin send a mobile push notification without taking a ProjectReference on
    /// Nop.Plugin.Notifications.Manager (which owns the concrete implementation and the actual
    /// Firebase call) - every plugin already depends on Nop.Services, so this is the shared seam.
    /// </summary>
    public interface IPushNotificationService
    {
        Task SendNotificationAsync(
            int customerId,
            NotificationType notificationType,
            string title,
            string body,
            IReadOnlyDictionary<string, string> data = null);

        Task SendNotificationAsync(
            Customer customer,
            NotificationType notificationType,
            string title,
            string body,
            IReadOnlyDictionary<string, string> data = null);
    }
}
