using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Company.Models;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Company.Company.Components
{
    /// <summary>
    /// Global delivery date picker view component - shared by the header widget zone and the
    /// checkout shipping-address zone, so both show the exact same button/modal (only one of
    /// the two ever actually renders on a given page: the header copy suppresses itself on the
    /// checkout page so there's a single button, in context, rather than a duplicate).
    /// </summary>
    [ViewComponent(Name = "GlobalDeliveryDatePicker")]
    public class GlobalDeliveryDatePickerViewComponent(
        IDeliveryTimeService deliveryTimeService,
        IWorkContext workContext,
        ICustomerService customerService)
        : NopViewComponent
    {
        /// <summary>
        /// Invoke view component
        /// </summary>
        /// <param name="widgetZone">Widget zone name</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the view component result
        /// </returns>
        public async Task<IViewComponentResult> InvokeAsync(string widgetZone)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            if (await customerService.IsGuestAsync(customer))
                return Content(string.Empty);

            var controllerName = ViewContext.RouteData.Values["controller"]?.ToString();
            var isCheckoutPage = string.Equals(controllerName, "Checkout", StringComparison.OrdinalIgnoreCase);

            // On the checkout page, the OpCheckoutShippingAddressBottom zone renders the
            // button instead of the header, so the customer sees it once, in context
            if (widgetZone == PublicWidgetZones.HeaderSelectors && isCheckoutPage)
                return Content(string.Empty);

            var model = new DeliveryDatePickerModel
            {
                MaxDaysAhead = await deliveryTimeService.GetMaxDaysAheadAsync(),
                IsCheckoutContext = widgetZone == PublicWidgetZones.OpCheckoutShippingAddressBottom
            };

            return View("~/Plugins/Company.Company/Views/Shared/Components/GlobalDeliveryDatePicker/Default.cshtml", model);
        }
    }
}
