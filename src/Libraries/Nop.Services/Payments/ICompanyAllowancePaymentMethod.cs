using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;

namespace Nop.Services.Payments
{
    public interface ICompanyAllowancePaymentMethod
    {
        Task<decimal> GetCustomerRemainingAllowance(DateTime date, Customer customer = null);
    }
}