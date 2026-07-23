using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;

namespace Nop.Services.Payments
{
    public interface ICompanyAllowancePaymentMethod
    {
        public Task<CustomerBalanceResult> GetCustomerRemainingAllowance(CustomerBalanceRequest customerBalanceRequest);
        public Task<bool> VoidAllowance(DateTime date, Customer customer = null);

        /// <summary>
        /// Whether this method would hide itself from the checkout payment-method list for
        /// the given cart (insufficient/no allowance, shippable-product setting, etc). Reused
        /// by AmeriaVPosPaymentProcessor.HidePaymentMethodAsync so the two payment methods stay
        /// mutually exclusive - an order is never split between allowance and card, so exactly
        /// one of the two should ever be a selectable radio button.
        /// </summary>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart);
    }
}