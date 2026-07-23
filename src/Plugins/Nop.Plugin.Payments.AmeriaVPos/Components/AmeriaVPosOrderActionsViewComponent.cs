using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Payments.AmeriaVPos.Domain;
using Nop.Plugin.Payments.AmeriaVPos.Models;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.AmeriaVPos.Components
{
    /// <summary>
    /// Renders the "Refund via AmeriaBank" / "Cancel via AmeriaBank" buttons on the admin
    /// Order Edit page, via the AdminWidgetZones.OrderDetailsBlock widget zone - deliberately
    /// NOT part of the stock _OrderDetails.Info.cshtml Refund/Void buttons (see design:
    /// real card money needs a separate, explicitly-confirmed action). Only renders for
    /// orders actually paid via AmeriaVPos with a completed charge to act on.
    /// </summary>
    [ViewComponent(Name = "AmeriaVPosOrderActions")]
    public class AmeriaVPosOrderActionsViewComponent : NopViewComponent
    {
        private readonly IOrderService _orderService;
        private readonly IPermissionService _permissionService;
        private readonly IRepository<AmeriaVPosPaymentAttempt> _attemptRepository;

        public AmeriaVPosOrderActionsViewComponent(
            IOrderService orderService,
            IPermissionService permissionService,
            IRepository<AmeriaVPosPaymentAttempt> attemptRepository)
        {
            _orderService = orderService;
            _permissionService = permissionService;
            _attemptRepository = attemptRepository;
        }

        public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
        {
            if (additionalData is not BaseNopEntityModel entityModel)
                return Content(string.Empty);

            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageOrders))
                return Content(string.Empty);

            var order = await _orderService.GetOrderByIdAsync(entityModel.Id);
            if (order == null || order.PaymentMethodSystemName != "Payments.AmeriaVPos")
                return Content(string.Empty);

            var attempt = await _attemptRepository.Table
                .Where(a => a.OrderId == order.Id)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            //nothing completed to refund/cancel - most orders on this method are either
            //fully allowance-covered (no attempt at all) or still pending/declined
            if (attempt?.Status != AmeriaVPosPaymentAttemptStatus.Paid)
                return Content(string.Empty);

            var model = new AmeriaVPosOrderActionsModel
            {
                OrderId = order.Id,
                ChargedAmount = attempt.ChargedAmount ?? attempt.RequestedAmount
            };

            return View("~/Plugins/Payments.AmeriaVPos/Views/OrderActions/OrderActions.cshtml", model);
        }
    }
}
