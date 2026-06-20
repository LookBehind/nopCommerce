using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Order
{
    /// <summary>
    /// Admin-only mobile-API surface for bulk order maintenance (e.g. clearing a
    /// customer's upcoming orders so their company allowance is freed). The mobile
    /// JWT authenticates the caller ([Authorize]); admin authorization is enforced
    /// the same way the Admin-area OrderController does it — via IPermissionService
    /// against AccessAdminPanel + ManageOrders (the [Authorize] attribute alone only
    /// proves authentication, not admin rights).
    /// </summary>
    [Produces("application/json")]
    [Route("api/admin/order")]
    [Authorize]
    public class AdminOrderApiController : BaseApiController
    {
        #region Fields

        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IPermissionService _permissionService;

        #endregion

        #region Ctor

        public AdminOrderApiController(IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IPermissionService permissionService)
        {
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _permissionService = permissionService;
        }

        #endregion

        #region Utilities

        /// <returns>
        /// True when the current customer may access the admin panel AND manage
        /// orders — mirrors the Admin-area OrderController's authorization.
        /// </returns>
        private async Task<bool> IsAdminAuthorizedAsync()
        {
            return await _permissionService.AuthorizeAsync(StandardPermissionProvider.AccessAdminPanel)
                && await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageOrders);
        }

        private IActionResult Forbidden() =>
            StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "Administrator access required." });

        #endregion

        #region Methods

        /// <summary>
        /// Lists orders for cleanup, filtered by customer and an optional ScheduleDate
        /// (delivery date) window. Already-cancelled and deleted orders are excluded.
        /// </summary>
        /// <param name="customerId">Optional. When omitted (0), all customers are included.</param>
        /// <param name="from">Optional. Lower bound (inclusive) on ScheduleDate (date part).</param>
        /// <param name="to">Optional. Upper bound (inclusive) on ScheduleDate (date part).</param>
        [HttpGet("scheduled")]
        public async Task<IActionResult> Scheduled(int customerId = 0, DateTime? from = null, DateTime? to = null)
        {
            if (!await IsAdminAuthorizedAsync())
                return Forbidden();

            // SearchOrdersAsync already filters by customer and excludes deleted
            // orders. The ScheduleDate window and cancelled-order exclusion are
            // applied with the async LINQ extensions over the result.
            var orders = await _orderService.SearchOrdersAsync(
                customerId: customerId,
                pageSize: int.MaxValue);

            var matched = await orders
                .Where(o => (OrderStatus)o.OrderStatusId != OrderStatus.Cancelled)
                .Where(o => !from.HasValue || o.ScheduleDate.Date >= from.Value.Date)
                .Where(o => !to.HasValue || o.ScheduleDate.Date <= to.Value.Date)
                .OrderBy(o => o.ScheduleDate)
                .Select(o => new
                {
                    id = o.Id,
                    customOrderNumber = o.CustomOrderNumber,
                    customerId = o.CustomerId,
                    scheduleDate = o.ScheduleDate,
                    orderTotal = o.OrderTotal,
                    orderStatusId = o.OrderStatusId,
                    paymentStatusId = o.PaymentStatusId
                })
                .ToListAsync();

            return Ok(new { success = true, count = matched.Count, orders = matched });
        }

        /// <summary>
        /// Cancels the given orders by id (nopCommerce cancel — sets status to
        /// Cancelled and reverses inventory/reward points; because used-allowance is
        /// computed only from non-cancelled paid orders, this frees the allowance).
        /// </summary>
        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel([FromBody] CancelOrdersRequest request)
        {
            if (!await IsAdminAuthorizedAsync())
                return Forbidden();

            if (request?.OrderIds == null || !request.OrderIds.Any())
                return Ok(new { success = false, message = "No orderIds provided." });

            var results = new List<object>();
            var cancelledCount = 0;
            foreach (var orderId in request.OrderIds.Distinct())
            {
                var order = await _orderService.GetOrderByIdAsync(orderId);
                if (order == null || order.Deleted)
                {
                    results.Add(new { id = orderId, cancelled = false, reason = "not found" });
                    continue;
                }

                if ((OrderStatus)order.OrderStatusId == OrderStatus.Cancelled)
                {
                    results.Add(new { id = orderId, cancelled = false, reason = "already cancelled" });
                    continue;
                }

                try
                {
                    await _orderProcessingService.CancelOrderAsync(order, request.NotifyCustomer);
                    cancelledCount++;
                    results.Add(new { id = orderId, cancelled = true });
                }
                catch (Exception ex)
                {
                    results.Add(new { id = orderId, cancelled = false, reason = ex.Message });
                }
            }

            return Ok(new { success = true, cancelled = cancelledCount, total = results.Count, results });
        }

        #endregion
    }

    /// <summary>Request body for <see cref="AdminOrderApiController.Cancel"/>.</summary>
    public class CancelOrdersRequest
    {
        public List<int> OrderIds { get; set; }

        /// <summary>Whether to send the order-cancelled notification. Defaults to false (silent cleanup).</summary>
        public bool NotifyCustomer { get; set; } = false;
    }
}
