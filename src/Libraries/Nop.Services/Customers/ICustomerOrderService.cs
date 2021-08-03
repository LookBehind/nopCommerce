using System;
using System.Threading.Tasks;

namespace Nop.Services.Customers
{
    public interface ICustomerOrderService
    {
        /// <summary>
        /// Get order total price.
        /// </summary>
        /// <param name="customer">customer.</param>
        /// <param name="from">from.</param>
        /// <param name="to">to.</param>
        /// <returns></returns>
        Task<decimal> GetCoustomerOrdersTotalAmount(int customerId, DateTime from, DateTime to);
    }
}
