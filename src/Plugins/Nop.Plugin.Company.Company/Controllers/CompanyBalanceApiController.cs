using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Payments;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Company.Company.Controllers
{
    /// <summary>
    /// Mobile-facing customer company allowance ("remaining balance") API.
    /// Kept separate from api/integration, which is reserved for third-party
    /// integrations (e.g. ServiceTitan), and from CustomerApiController's
    /// company-details endpoint, which only exposes the company's static
    /// configured limit.
    /// </summary>
    [Produces("application/json")]
    [Route("api/company")]
    [Authorize]
    public class CompanyBalanceApiController(
        ICompanyAllowancePaymentMethod companyAllowancePaymentMethod,
        IWorkContext workContext)
        : BaseApiController
    {
        /// <summary>
        /// Gets the current customer's remaining company allowance for the active period.
        /// Computed live from paid orders on every call - there is no cached/stored
        /// balance to refresh, so the mobile client should just re-fetch this after
        /// a successful checkout.
        /// </summary>
        [HttpGet("balance")]
        public async Task<IActionResult> GetBalanceAsync()
        {
            var customer = await workContext.GetCurrentCustomerAsync();

            var balanceResult = await companyAllowancePaymentMethod.GetCustomerRemainingAllowance(
                new CustomerBalanceRequest
                {
                    Customer = customer,
                    OrderDateUtc = DateTime.UtcNow
                });

            if (balanceResult == null)
            {
                return Ok(new { success = true, hasBalance = false });
            }

            var usedBalance = balanceResult.TotalAllowance - balanceResult.RemainingAllowance;

            return Ok(new
            {
                success = true,
                hasBalance = true,
                totalBalance = balanceResult.TotalAllowance,
                remainingBalance = balanceResult.RemainingAllowance,
                usedBalance,
                refreshCadence = balanceResult.RefreshCadence.ToString(),
                refreshesInDays = balanceResult.RefreshedAfter.Days
            });
        }
    }
}
