using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Companies;
using Nop.Plugin.Payments.CheckMoneyOrder.Models;
using Nop.Services.Catalog;
using Nop.Services.Payments;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.CheckMoneyOrder.Components
{
    [ViewComponent(Name = "Balance")]
    public class BalanceViewComponent : NopViewComponent
    {
        private readonly ICompanyAllowancePaymentMethod _companyAllowancePaymentMethod;
        private readonly IWorkContext _workContext;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IStoreContext _storeContext;
        
        public BalanceViewComponent(
            ICompanyAllowancePaymentMethod companyAllowancePaymentMethod, 
            IWorkContext workContext, 
            IPriceFormatter priceFormatter, 
            IStoreContext storeContext)
        {
            _companyAllowancePaymentMethod = companyAllowancePaymentMethod;
            _workContext = workContext;
            _priceFormatter = priceFormatter;
            _storeContext = storeContext;
        }
        
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<IViewComponentResult> InvokeAsync(string widgetZone)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            
            // TODO: fix when schedule date is set through a separate controller (date popup)
            var customerBalanceResult = 
                await _companyAllowancePaymentMethod.GetCustomerRemainingAllowance(new CustomerBalanceRequest()
                {
                    Customer = customer,
                    OrderDateUtc = DateTime.UtcNow
                });

            BalanceModel model;
            
            if (customerBalanceResult == null)
            {
                model = new BalanceModel
                {
                    HasBalance = false
                };
            }
            else
            {
                var usedBalance = customerBalanceResult.TotalAllowance - customerBalanceResult.RemainingAllowance;
                var daysInPeriod = customerBalanceResult.RefreshCadence switch
                {
                    AmountLimitType.Daily => 1,
                    AmountLimitType.Weekly => 7,
                    AmountLimitType.Monthly => DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month),
                    _ => throw new ArgumentOutOfRangeException(nameof(customerBalanceResult.RefreshCadence))
                };
                var recommendedAverageSpending = customerBalanceResult.TotalAllowance / daysInPeriod;
                var daysRemaining = customerBalanceResult.RefreshedAfter.Days;
                var daysPassed = daysInPeriod - daysRemaining;
                var recommendedSpendingUntilNow = daysPassed * recommendedAverageSpending;
                
                model = new BalanceModel
                {
                    HasBalance = true,
                    TotalBalance = customerBalanceResult.TotalAllowance,
                    RemainingBalance = customerBalanceResult.RemainingAllowance,
                    RemainingBalanceFormatted = await _priceFormatter.FormatPriceAsync(customerBalanceResult.RemainingAllowance),
                    UsedBalance = usedBalance,
                    UsedBalanceFormatted = await _priceFormatter.FormatPriceAsync(usedBalance),
                    RecommendedSpending = recommendedSpendingUntilNow,
                    RecommendedSpendingFormatted = await _priceFormatter.FormatPriceAsync(recommendedSpendingUntilNow)
                };
            }

            return View("~/Plugins/Payments.CheckMoneyOrder/Views/Balance.cshtml", model);
        }
    }
}
