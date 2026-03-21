using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Company.Models;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Customers;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Company.Company.Components
{
    /// <summary>
    /// Global delivery date picker view component
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
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the view component result
        /// </returns>
        public async Task<IViewComponentResult> InvokeAsync()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            if (await customerService.IsGuestAsync(customer))
                return Content(string.Empty);

            var model = new DeliveryDatePickerModel
            {
                MaxDaysAhead = await deliveryTimeService.GetMaxDaysAheadAsync()
            };

            return View("~/Plugins/Company.Company/Views/Shared/Components/GlobalDeliveryDatePicker/Default.cshtml", model);
        }
    }
}
