using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Catalog;
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
        IWorkContext workContext,
        IStoreContext storeContext,
        IDeliveryTimeStorageService deliveryTimeStorageService,
        IPriceFormatter priceFormatter)
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
            var store = await storeContext.GetCurrentStoreAsync();

            // The allowance is a per-day cap, and the customer's already-selected
            // delivery date (the same value the actual order placement checks
            // against - see AmeriaVPosPaymentService/CheckMoneyOrderPaymentProcessor,
            // both keyed on order.ScheduleDate) is what actually matters here, not
            // "today". Falling back to DateTime.UtcNow was showing the checkout
            // warning (and this same value on the profile balance card) based on
            // today's usage even when the order is scheduled for a day with its own
            // untouched allowance - e.g. today's cap fully used, but the order is
            // scheduled for a future day with nothing booked against it yet.
            var selectedDeliveryTime = await deliveryTimeStorageService.GetSelectedDeliveryTimeAsync(customer, store.Id);

            var balanceResult = await companyAllowancePaymentMethod.GetCustomerRemainingAllowance(
                new CustomerBalanceRequest
                {
                    Customer = customer,
                    OrderDateUtc = selectedDeliveryTime ?? DateTime.UtcNow
                });

            if (balanceResult == null)
            {
                return Ok(new { success = true, hasBalance = false });
            }

            var usedBalance = balanceResult.TotalAllowance - balanceResult.RemainingAllowance;
            var recommendedSpending = balanceResult.GetRecommendedSpendingUntilNow();

            // Real store currency (e.g. AMD), not hardcoded - same RegionInfo(DisplayLocale)
            // pattern CommonModelFactory.PrepareCurrencySelectorModelAsync already uses for
            // the storefront's own currency-selector dropdown, falling back to the ISO code
            // the same way that does when DisplayLocale isn't set.
            var currency = await workContext.GetWorkingCurrencyAsync();
            var currencySymbol = !string.IsNullOrEmpty(currency.DisplayLocale)
                ? new RegionInfo(currency.DisplayLocale).CurrencySymbol
                : currency.CurrencyCode;

            return Ok(new
            {
                success = true,
                hasBalance = true,
                totalBalance = balanceResult.TotalAllowance,
                remainingBalance = balanceResult.RemainingAllowance,
                usedBalance,
                recommendedSpending,
                currencySymbol,
                // Full IPriceFormatter output (same formatter every other price in the app
                // uses) for the breakdown panel, which has room for the real formatted
                // amount - the header ring's compact "2.5k" stays a client-side abbreviation
                // of the real number, with currencySymbol appended.
                remainingBalanceFormatted = await priceFormatter.FormatPriceAsync(balanceResult.RemainingAllowance),
                usedBalanceFormatted = await priceFormatter.FormatPriceAsync(usedBalance),
                recommendedSpendingFormatted = await priceFormatter.FormatPriceAsync(recommendedSpending),
                refreshCadence = balanceResult.RefreshCadence.ToString(),
                refreshesInDays = balanceResult.RefreshedAfter.Days
            });
        }
    }
}
