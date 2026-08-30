using System.Threading.Tasks;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Marks a single vendor's portion of a (possibly multi-vendor) order as delivered.
/// Reuses nopCommerce's built-in Shipment/ShipmentItem model, scoping one Shipment per
/// vendor to just that vendor's OrderItems - Order.OrderStatus only becomes Complete once
/// every vendor's shipment is delivered, instead of on the first vendor's "delivered" event.
/// Shared by the Telegram bot command handler and the vendor delivery Mini App controller,
/// which previously duplicated this logic (see TelegramNotificationSenderTask,
/// VendorDeliveryAppController).
/// </summary>
public interface IVendorOrderDeliveryService
{
    /// <summary>
    /// Marks <paramref name="vendor"/>'s items on <paramref name="order"/> as delivered.
    /// Idempotent: returns false without side effects if this vendor already marked their
    /// portion of this order delivered, or if the vendor has no items on the order.
    /// </summary>
    /// <returns>True if this call newly marked the vendor's portion delivered.</returns>
    Task<bool> MarkVendorDeliveredAsync(Order order, Vendor vendor);

    /// <summary>
    /// Whether <paramref name="vendor"/> has already marked their portion of
    /// <paramref name="order"/> delivered (read-only, for board/status display).
    /// </summary>
    Task<bool> IsVendorPortionDeliveredAsync(Order order, Vendor vendor);
}
