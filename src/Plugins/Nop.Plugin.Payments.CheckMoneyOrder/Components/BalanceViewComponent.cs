using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.CheckMoneyOrder.Models;
using Nop.Services.Catalog;
using Nop.Services.Directory;
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
        private readonly ICurrencyService _currencyService;
        
        public BalanceViewComponent(
            ICompanyAllowancePaymentMethod companyAllowancePaymentMethod, 
            IWorkContext workContext, 
            IPriceFormatter priceFormatter, 
            ICurrencyService currencyService)
        {
            _companyAllowancePaymentMethod = companyAllowancePaymentMethod;
            _workContext = workContext;
            _priceFormatter = priceFormatter;
            _currencyService = currencyService;
        }
        
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<IViewComponentResult> InvokeAsync(string widgetZone)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            
            // TODO: fix when schedule date is set through a separate controller (date popup)
            var (remainingAllowance, refreshCadence) = 
                await _companyAllowancePaymentMethod.GetCustomerRemainingAllowance(DateTime.UtcNow, customer);

            var formattedPrice = await _priceFormatter.FormatPriceAsync(remainingAllowance);
            
            var model = new BalanceModel
            {
                Balance = formattedPrice,
                RefreshCadence = refreshCadence.ToString().ToLower()
            };

            return View("~/Plugins/Payments.CheckMoneyOrder/Views/Balance.cshtml", model);
        }
    }
}
