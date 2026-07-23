using System.Collections.Generic;
using Nop.Web.Framework.Models;

namespace Nop.Web.Models.Checkout
{
    public partial record CheckoutConfirmModel : BaseNopModel
    {
        public CheckoutConfirmModel()
        {
            Warnings = new List<string>();
        }

        public bool TermsOfServiceOnOrderConfirmPage { get; set; }
        public bool TermsOfServicePopup { get; set; }
        public string MinOrderTotalWarning { get; set; }
        /// <summary>
        /// Set when the cart total exceeds the customer's remaining company allowance -
        /// confirming will redirect them to AmeriaVPos to pay the full order by card (see
        /// Nop.Plugin.Company.Company's CheckoutModelFactory_Overriden). Mirrors the
        /// mobile app's Confirm-order sheet warning.
        /// </summary>
        public string AllowanceExceededWarning { get; set; }

        public IList<string> Warnings { get; set; }
    }
}