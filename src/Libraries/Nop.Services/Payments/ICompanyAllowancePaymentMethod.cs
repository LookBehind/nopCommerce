using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Customers;

namespace Nop.Services.Payments
{
    public interface ICompanyAllowancePaymentMethod
    {
        public Task<CustomerBalanceResult> GetCustomerRemainingAllowance(CustomerBalanceRequest customerBalanceRequest);
        public Task<bool> VoidAllowance(DateTime date, Customer customer = null);
    }
}