using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Company.Company.Components
{
    /// <summary>
    /// Global delivery time validation view component
    /// </summary>
    [ViewComponent(Name = "GlobalDeliveryTimeValidation")]
    public class GlobalDeliveryTimeValidationViewComponent : NopViewComponent
    {
        #region Methods

        /// <summary>
        /// Invoke view component
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the view component result
        /// </returns>
        public async Task<IViewComponentResult> InvokeAsync()
        {
            // This component just renders the validation JavaScript
            return View("~/Plugins/Company.Company/Views/Shared/Components/GlobalDeliveryTimeValidation/Default.cshtml");
        }

        #endregion
    }
}

