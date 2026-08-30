using System;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Vendors;
using Nop.Services.Orders;
using Nop.Services.Shipping;

namespace Nop.Plugin.Notifications.Manager.Services;

public class VendorOrderDeliveryService : IVendorOrderDeliveryService
{
    private readonly IOrderService _orderService;
    private readonly IShipmentService _shipmentService;
    private readonly IOrderProcessingService _orderProcessingService;

    public VendorOrderDeliveryService(
        IOrderService orderService,
        IShipmentService shipmentService,
        IOrderProcessingService orderProcessingService)
    {
        _orderService = orderService;
        _shipmentService = shipmentService;
        _orderProcessingService = orderProcessingService;
    }

    public async Task<bool> IsVendorPortionDeliveredAsync(Order order, Vendor vendor)
    {
        var shipments = await _shipmentService.GetShipmentsByOrderIdAsync(order.Id, vendorId: vendor.Id);
        return shipments.Any(s => s.DeliveryDateUtc.HasValue);
    }

    public async Task<bool> MarkVendorDeliveredAsync(Order order, Vendor vendor)
    {
        var existingShipments = await _shipmentService.GetShipmentsByOrderIdAsync(order.Id, vendorId: vendor.Id);
        if (existingShipments.Any(s => s.DeliveryDateUtc.HasValue))
            return false; // this vendor already marked their portion of this order delivered

        var vendorItems = await _orderService.GetOrderItemsAsync(order.Id, vendorId: vendor.Id);
        if (!vendorItems.Any())
            return false; // this vendor has no items on this order - nothing for them to deliver

        // Reuse a not-yet-delivered shipment for this vendor if one already exists (e.g. a
        // previous attempt inserted it but crashed before Ship/DeliverAsync), otherwise create
        // one scoped to just this vendor's items.
        var shipment = existingShipments.FirstOrDefault();
        if (shipment == null)
        {
            shipment = new Shipment
            {
                OrderId = order.Id,
                AdminComment = $"Auto-created: vendor '{vendor.Name}' delivery confirmation",
                CreatedOnUtc = DateTime.UtcNow
            };
            await _shipmentService.InsertShipmentAsync(shipment);

            foreach (var item in vendorItems)
            {
                await _shipmentService.InsertShipmentItemAsync(new ShipmentItem
                {
                    ShipmentId = shipment.Id,
                    OrderItemId = item.Id,
                    Quantity = item.Quantity,
                    WarehouseId = 0
                });
            }
        }

        // MySnacks has no separate "shipped" checkpoint - a vendor only ever reports
        // "delivered" - so ship immediately before delivering, satisfying DeliverAsync's
        // precondition that the shipment already be marked shipped.
        if (!shipment.ShippedDateUtc.HasValue)
            await _orderProcessingService.ShipAsync(shipment, notifyCustomer: false);

        await _orderProcessingService.DeliverAsync(shipment, notifyCustomer: false);

        return true;
    }
}
