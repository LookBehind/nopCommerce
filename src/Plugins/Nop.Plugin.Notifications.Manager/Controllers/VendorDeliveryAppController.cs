using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Vendors;
using Nop.Web.Controllers;

namespace Nop.Plugin.Notifications.Manager.Controllers;

/// <summary>
/// Backs the Telegram Mini App vendor delivery board. Auth is deliberately NOT the app's normal
/// JWT `[Authorize]` scheme - see docs/plans/2026-07-28-vendor-delivery-mini-app.md §4.3: a signed
/// board token (query string) scopes access to one vendor+store, and Telegram's own signed
/// `initData` (header) proves the request genuinely came from a Telegram Mini App launch. Anyone
/// in the vendor's chat can use the link - that's the intended access model, not a gap.
/// </summary>
[Produces("application/json")]
[Route("api/vendor-delivery-app")]
[AllowAnonymous]
public class VendorDeliveryAppController : BaseApiController
{
    private const string InitDataHeaderName = "X-Telegram-Init-Data";

    private readonly ITelegramMiniAppAuthService _telegramMiniAppAuthService;
    private readonly IOrderService _orderService;
    private readonly IVendorService _vendorService;
    private readonly IAddressService _addressService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly PushNotificationService _pushNotificationService;
    private readonly ISettingService _settingService;

    public VendorDeliveryAppController(
        ITelegramMiniAppAuthService telegramMiniAppAuthService,
        IOrderService orderService,
        IVendorService vendorService,
        IAddressService addressService,
        ICustomerService customerService,
        IProductService productService,
        PushNotificationService pushNotificationService,
        ISettingService settingService)
    {
        _telegramMiniAppAuthService = telegramMiniAppAuthService;
        _orderService = orderService;
        _vendorService = vendorService;
        _addressService = addressService;
        _customerService = customerService;
        _productService = productService;
        _pushNotificationService = pushNotificationService;
        _settingService = settingService;
    }

    public record OrderCardModel(int Id, string Slot, string Addr, string Addr2, List<string> Items, bool Delivered);
    public record BoardResponse(string VendorName, List<OrderCardModel> Orders);

    private record AuthorizeResult(bool Authorized, int VendorId, int StoreId, IActionResult Error);

    private async Task<AuthorizeResult> TryAuthorize(string token)
    {
        var notificationManagerSettings = await _settingService.LoadSettingAsync<NotificationManagerSettings>();
        if (!notificationManagerSettings.VendorDeliveryMiniAppEnabled)
            return new AuthorizeResult(false, 0, 0, NotFound());

        var initData = Request.Headers[InitDataHeaderName].ToString();
        if (!_telegramMiniAppAuthService.TryValidateInitData(initData, out _))
            return new AuthorizeResult(false, 0, 0, Unauthorized(new ErrorMessage("Missing or invalid Telegram session")));

        if (!_telegramMiniAppAuthService.TryValidateBoardToken(token, out var vendorId, out var storeId))
            return new AuthorizeResult(false, 0, 0, Unauthorized(new ErrorMessage("Missing, invalid, or expired board link")));

        return new AuthorizeResult(true, vendorId, storeId, null);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] string token)
    {
        var auth = await TryAuthorize(token);
        if (!auth.Authorized)
            return auth.Error;
        var (vendorId, storeId) = (auth.VendorId, auth.StoreId);

        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (vendor == null)
            return NotFound();

        var pendingStatuses = new List<int> { (int)OrderStatus.Processing, (int)OrderStatus.Pending };
        var orders = await _orderService.SearchOrdersAsync(
            storeId: storeId,
            vendorId: vendorId,
            osIds: pendingStatuses,
            schedulDate: DateTime.UtcNow.Date);

        var cards = new List<OrderCardModel>();
        foreach (var order in orders)
        {
            var slot = TelegramNotificationSenderTask.GetLocalScheduleTime(order).ToString("HH:mm");
            var vendorItems = await _orderService.GetOrderItemsAsync(order.Id, vendorId: vendorId);
            if (!vendorItems.Any())
                continue; // this vendor has no items in the order, not theirs to show

            var products = await _productService.GetProductsByIdsAsync(vendorItems.Select(i => i.ProductId).ToArray());
            var productNamesById = products.ToDictionary(p => p.Id, p => p.Name);
            var itemLabels = vendorItems
                .Select(i => $"{(productNamesById.TryGetValue(i.ProductId, out var name) ? name : "Item")} x{i.Quantity}")
                .ToList();

            var address = order.ShippingAddressId.HasValue
                ? await _addressService.GetAddressByIdAsync(order.ShippingAddressId.Value)
                : null;

            cards.Add(new OrderCardModel(
                order.Id,
                slot,
                address?.Address1 ?? "Unknown location",
                address?.Address2,
                itemLabels,
                Delivered: false));
        }

        return Ok(new BoardResponse(vendor.Name, cards.OrderBy(c => c.Slot).ToList()));
    }

    [HttpPost("orders/{orderId:int}/deliver")]
    public async Task<IActionResult> MarkDelivered(int orderId, [FromQuery] string token)
    {
        var auth = await TryAuthorize(token);
        if (!auth.Authorized)
            return auth.Error;
        var (vendorId, storeId) = (auth.VendorId, auth.StoreId);

        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order == null || order.StoreId != storeId)
            return NotFound();

        var vendorItems = await _orderService.GetOrderItemsAsync(order.Id, vendorId: vendorId);
        if (!vendorItems.Any())
            return NotFound(); // order doesn't belong to this vendor

        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);

        // Deliberately duplicates TelegramNotificationSenderTask.HandleBotCommandDeliveredEvent's
        // marking logic rather than sharing it - see design §6 decision 4.
        order.OrderStatus = OrderStatus.Complete;
        await _orderService.UpdateOrderAsync(order);

        await _pushNotificationService.SendNotificationAsync(order.CustomerId,
            NotificationType.OrderStatusChange,
            "Order delivered",
            $"Your order from vendor {vendor.Name} has been delivered",
            new Dictionary<string, string>
            {
                { "order_delivered", "true" },
                { "orderId", order.Id.ToString() },
                { "url", $"Order/{order.Id}" }
            });

        return Ok();
    }
}
