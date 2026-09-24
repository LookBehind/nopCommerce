using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Seo;
using Nop.Services.Vendors;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Order
{
    /// <summary>
    /// mobile-v2's own order-history/order-action surface - api/v2/order, versioned
    /// alongside api/v2/catalog/cart/checkout. Mirrors the real v1 mobile backend's
    /// order endpoints (OrderApiController's get-todays-orders/get-previous-orders/
    /// get-upcoming-orders/list/reorder/order-rating/payment-status/initiate-payment)
    /// against the same real services (IOrderService, IAmeriaVPosPaymentService, etc.)
    /// rather than proxying v1's own HTTP endpoints, with two deliberate differences:
    ///
    /// 1. "list" is the only real read endpoint here (v1 also still exposes three
    ///    older today/upcoming/previous-only endpoints kept for its already-shipped
    ///    app builds) - it alone already returns the same three buckets in one call,
    ///    plus real pagination for past orders and single-order lookup.
    ///
    /// 2. "reorder" here actually re-adds the still-valid items into the real
    ///    api/v2/cart (IShoppingCartService.AddToCartAsync, reusing the order item's
    ///    own real AttributesXml directly - no need to reconstruct it from a
    ///    mapping/value id breakdown) instead of v1's validate-only response that
    ///    expects the client to loop back and re-add each item itself.
    ///
    /// order-rating also gets a real ownership check v1's own endpoint is missing
    /// (any authenticated customer could rate an arbitrary orderId there).
    [Produces("application/json")]
    [Route("api/v2/order")]
    [Authorize]
    public class OrderV2ApiController(
        IOrderService orderService,
        IOrderProcessingService orderProcessingService,
        IShoppingCartService shoppingCartService,
        IProductService productService,
        IPictureService pictureService,
        IVendorService vendorService,
        IAddressService addressService,
        IAmeriaVPosPaymentService ameriaVPosPaymentService,
        IPriceFormatter priceFormatter,
        ILocalizationService localizationService,
        IUrlRecordService urlRecordService,
        IWorkContext workContext,
        IStoreContext storeContext)
        : BaseApiController
    {
        private const int ThumbnailSize = 300;
        // The exact same round-trip-safe local-date format api/v2/checkout already
        // uses for ScheduleDate - pure date display, not a money/count value, so the
        // client formats it (see mobile's formatScheduleDateLabel) rather than the
        // backend baking a "Today, 14:00"-style string in.
        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

        public class OrderItemV2Model
        {
            public int Id { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ImageUrl { get; set; }
            public string VendorName { get; set; }
            public int Quantity { get; set; }
            // Real, already-formatted attribute description (e.g. "Serving size: Half") -
            // nopCommerce's own OrderItem.AttributeDescription, not reconstructed.
            public string AttributeInfo { get; set; }
            public string UnitPrice { get; set; }
            public string LineTotal { get; set; }
            public int? UserRating { get; set; }
        }

        public class OrderV2Model
        {
            public int Id { get; set; }
            public string CustomOrderNumber { get; set; }
            public string ScheduleDate { get; set; }
            public string CreatedOn { get; set; }
            public string OrderStatus { get; set; }
            public string PaymentStatus { get; set; }
            public string ShippingStatus { get; set; }
            public string DeliveryAddress { get; set; }
            public decimal OrderTotalValue { get; set; }
            public string OrderTotal { get; set; }
            public bool RequiresPayment { get; set; }
            public decimal AmountDueValue { get; set; }
            public string AmountDueFormatted { get; set; }
            public bool IsReturnRequestAllowed { get; set; }
            public int Rating { get; set; }
            public string RatingText { get; set; }
            public IList<OrderItemV2Model> Items { get; set; } = new List<OrderItemV2Model>();
        }

        public class OrderRatingV2Model
        {
            public int OrderId { get; set; }
            public int Rating { get; set; }
            public string RatingText { get; set; }
        }

        public class ReorderItemResultV2Model
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetOrders(string segment = null, int page = 0, int pageSize = 20, int? orderId = null)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            // The current customer's own review rating per order item (fetched once,
            // not once per order mapped - same hoisting v1's own endpoints do).
            var customerReviews = (await productService.GetAllProductReviewsAsync(customerId: customer.Id))
                .Where(r => r.OrderItemId.HasValue)
                .ToDictionary(r => r.OrderItemId.Value, r => r.Rating);

            if (orderId.HasValue)
            {
                var order = await orderService.GetOrderByIdAsync(orderId.Value);
                if (order == null || order.Deleted || order.CustomerId != customer.Id)
                    return Ok(new { success = false });

                return Ok(new { success = true, order = await MapOrderAsync(order, customerReviews) });
            }

            if (pageSize <= 0)
                pageSize = 20;
            if (page < 0)
                page = 0;

            var allOrders = await orderService.SearchOrdersAsync(customerId: customer.Id, sortByDeliveryDate: true);

            if (string.Equals(segment, "past", StringComparison.OrdinalIgnoreCase))
            {
                var pastOrders = allOrders.Where(o => o.ScheduleDate.Date < DateTime.Now.Date)
                    .OrderByDescending(o => o.ScheduleDate).ToList();

                var totalCount = pastOrders.Count;
                var pageItems = pastOrders.Skip(page * pageSize).Take(pageSize).ToList();
                var hasMore = (page + 1) * pageSize < totalCount;

                var items = new List<OrderV2Model>();
                foreach (var order in pageItems)
                    items.Add(await MapOrderAsync(order, customerReviews));

                return Ok(new { success = true, items, page, pageSize, hasMore, totalCount });
            }

            // Combined initial load: upcoming + today + first page of past, each its
            // own real ScheduleDate-ordered bucket (same three buckets v1's three
            // separate get-todays/get-upcoming/get-previous-orders endpoints return).
            var upcomingOrders = allOrders.Where(o => o.ScheduleDate.Date > DateTime.Now.Date)
                .OrderByDescending(o => o.ScheduleDate).ToList();
            var todayOrders = allOrders.Where(o => o.ScheduleDate.Date == DateTime.Now.Date)
                .OrderByDescending(o => o.ScheduleDate).ToList();
            var previousOrders = allOrders.Where(o => o.ScheduleDate.Date < DateTime.Now.Date)
                .OrderByDescending(o => o.ScheduleDate).ToList();

            var upcoming = new List<OrderV2Model>();
            foreach (var order in upcomingOrders)
                upcoming.Add(await MapOrderAsync(order, customerReviews));

            var today = new List<OrderV2Model>();
            foreach (var order in todayOrders)
                today.Add(await MapOrderAsync(order, customerReviews));

            var pastTotalCount = previousOrders.Count;
            var pastPageItems = previousOrders.Take(pageSize).ToList();
            var pastHasMore = pageSize < pastTotalCount;

            var past = new List<OrderV2Model>();
            foreach (var order in pastPageItems)
                past.Add(await MapOrderAsync(order, customerReviews));

            return Ok(new
            {
                success = true,
                upcoming,
                today,
                past = new { items = past, page = 0, pageSize, hasMore = pastHasMore, totalCount = pastTotalCount }
            });
        }

        // Real re-order: re-adds each still-orderable item straight into the real
        // api/v2/cart (same AddToCartAsync CartV2ApiController.AddItem uses), reusing
        // the order item's own real AttributesXml as-is - unlike v1's version, the
        // mobile client doesn't need a second round trip per item to actually add
        // anything. It should refetch api/v2/cart afterward to pick up the result.
        [HttpPost("{orderId}/reorder")]
        public async Task<IActionResult> Reorder(int orderId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();
            var order = await orderService.GetOrderByIdAsync(orderId);

            if (order == null || order.Deleted || order.CustomerId != customer.Id)
                return Ok(new { success = false, message = await localizationService.GetResourceAsync("Order.NoOrderFound") });

            var results = new List<ReorderItemResultV2Model>();
            foreach (var orderItem in await orderService.GetOrderItemsAsync(order.Id))
            {
                var product = await productService.GetProductByIdAsync(orderItem.ProductId);
                if (product == null || product.Deleted || !product.Published)
                {
                    results.Add(new ReorderItemResultV2Model
                    {
                        ProductId = orderItem.ProductId,
                        ProductName = product?.Name,
                        Success = false,
                        Message = $"{product?.Name ?? "This item"} is no longer available"
                    });
                    continue;
                }

                var warnings = await shoppingCartService.AddToCartAsync(
                    customer, product, ShoppingCartType.ShoppingCart, store.Id, orderItem.AttributesXml, quantity: orderItem.Quantity);

                results.Add(new ReorderItemResultV2Model
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Success = warnings.Count == 0,
                    Message = warnings.Count > 0 ? string.Join(", ", warnings) : null
                });
            }

            return Ok(new
            {
                success = true,
                message = await localizationService.GetResourceAsync("Order.ReOrdered"),
                results
            });
        }

        [HttpPost("order-rating")]
        public async Task<IActionResult> RateOrder([FromBody] OrderRatingV2Model model)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var order = await orderService.GetOrderByIdAsync(model?.OrderId ?? 0);

            if (order == null || order.Deleted || order.CustomerId != customer.Id)
                return Ok(new { success = false, message = await localizationService.GetResourceAsync("Order.Rating.Failed") });

            order.Rating = model.Rating;
            order.RatingText = model.RatingText;
            await orderService.UpdateOrderAsync(order);

            return Ok(new { success = true, message = await localizationService.GetResourceAsync("Order.Rating.Added") });
        }

        [HttpGet("{orderId}/payment-status")]
        public async Task<IActionResult> GetPaymentStatus(int orderId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var order = await orderService.GetOrderByIdAsync(orderId);

            if (order == null || order.Deleted || order.CustomerId != customer.Id)
                return Ok(new { success = false });

            var result = await ameriaVPosPaymentService.GetLatestAttemptStatusAsync(order);

            return Ok(new
            {
                success = true,
                status = result.Status,
                resolved = result.Resolved,
                amountDueValue = result.AmountDue,
                amountDueFormatted = await priceFormatter.FormatPriceAsync(result.AmountDue)
            });
        }

        // Re-triggers AmeriaVPos payment for an existing Pending self-pay order - the
        // real "Pay" action for an order OrderV2Model.RequiresPayment flagged. Mirrors
        // v1's own InitiatePaymentAsync (same InitiateOrCompletePaymentAsync checkout
        // itself uses), which re-checks the customer's current allowance too - if it's
        // freed up since the order was placed this can resolve it as Paid outright.
        [HttpPost("{orderId}/initiate-payment")]
        public async Task<IActionResult> InitiatePayment(int orderId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var order = await orderService.GetOrderByIdAsync(orderId);

            if (order == null || order.Deleted || order.CustomerId != customer.Id)
                return Ok(new { success = false, message = await localizationService.GetResourceAsync("Order.NoOrderFound") });

            if (order.PaymentStatus == PaymentStatus.Paid)
                return Ok(new { success = true, requiresPayment = false, orderId = order.Id });

            var paymentResult = await ameriaVPosPaymentService.InitiateOrCompletePaymentAsync(order, "Mobile");

            if (!paymentResult.RequiresPayment)
                return Ok(new { success = true, requiresPayment = false, orderId = order.Id });

            var message = string.IsNullOrEmpty(paymentResult.PaymentUrl)
                ? "The card payment couldn't be started. Please try again."
                : null;

            return Ok(new
            {
                success = false,
                requiresPayment = true,
                orderId = order.Id,
                paymentUrl = paymentResult.PaymentUrl,
                amountDueValue = paymentResult.AmountDue,
                amountDueFormatted = await priceFormatter.FormatPriceAsync(paymentResult.AmountDue),
                message
            });
        }

        private async Task<OrderV2Model> MapOrderAsync(Nop.Core.Domain.Orders.Order order, IDictionary<int, int> customerReviews)
        {
            var requiresPayment = order.PaymentStatus == PaymentStatus.Pending && order.PaymentMethodSystemName == "Payments.AmeriaVPos";
            var amountDue = requiresPayment ? order.OrderTotal : 0M;

            var model = new OrderV2Model
            {
                Id = order.Id,
                CustomOrderNumber = order.CustomOrderNumber,
                ScheduleDate = order.ScheduleDate.ToString(DateFormat),
                CreatedOn = order.CreatedOnUtc.ToString(DateFormat),
                OrderStatus = await localizationService.GetLocalizedEnumAsync(order.OrderStatus),
                PaymentStatus = await localizationService.GetLocalizedEnumAsync(order.PaymentStatus),
                ShippingStatus = await localizationService.GetLocalizedEnumAsync(order.ShippingStatus),
                DeliveryAddress = await FormatDeliveryAddressAsync(order),
                OrderTotalValue = order.OrderTotal,
                OrderTotal = await priceFormatter.FormatPriceAsync(order.OrderTotal),
                RequiresPayment = requiresPayment,
                AmountDueValue = amountDue,
                AmountDueFormatted = await priceFormatter.FormatPriceAsync(amountDue),
                IsReturnRequestAllowed = await orderProcessingService.IsReturnRequestAllowedAsync(order),
                Rating = order.Rating,
                RatingText = order.RatingText
            };

            foreach (var orderItem in await orderService.GetOrderItemsAsync(order.Id))
            {
                var product = await productService.GetProductByIdAsync(orderItem.ProductId);
                var pictures = await pictureService.GetPicturesByProductIdAsync(orderItem.ProductId, 1);
                var imageUrl = pictures.Count > 0
                    ? (await pictureService.GetPictureUrlAsync(pictures[0], ThumbnailSize)).Url
                    : null;
                var vendor = product != null ? await vendorService.GetVendorByProductIdAsync(product.Id) : null;

                model.Items.Add(new OrderItemV2Model
                {
                    Id = orderItem.Id,
                    ProductId = orderItem.ProductId,
                    ProductName = product?.Name,
                    ImageUrl = imageUrl,
                    VendorName = vendor?.Name,
                    Quantity = orderItem.Quantity,
                    AttributeInfo = orderItem.AttributeDescription,
                    UnitPrice = await priceFormatter.FormatPriceAsync(orderItem.UnitPriceInclTax),
                    LineTotal = await priceFormatter.FormatPriceAsync(orderItem.PriceInclTax),
                    UserRating = customerReviews.TryGetValue(orderItem.Id, out var userRating) ? userRating : null
                });
            }

            return model;
        }

        private async Task<string> FormatDeliveryAddressAsync(Nop.Core.Domain.Orders.Order order)
        {
            var addressId = order.PickupInStore ? order.PickupAddressId : order.ShippingAddressId;
            if (!addressId.HasValue)
                return string.Empty;

            var address = await addressService.GetAddressByIdAsync(addressId.Value);
            if (address == null)
                return string.Empty;

            return string.Join(", ", new[] { address.Address1, address.Address2, address.City }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }
}
