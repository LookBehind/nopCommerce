using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Company.Models;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Companies;
using Nop.Web.Framework.Components;
using TimeZoneConverter;

namespace Nop.Plugin.Company.Company.Components
{
    /// <summary>
    /// Global delivery date picker view component
    /// </summary>
    [ViewComponent(Name = "GlobalDeliveryDatePicker")]
    public class GlobalDeliveryDatePickerViewComponent : NopViewComponent
    {
        #region Fields

        private readonly IDeliveryTimeService _deliveryTimeService;
        private readonly IGlobalDeliveryTimeValidationService _globalValidationService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly IWorkContext _workContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICompanyService _companyService;

        #endregion

        #region Ctor

        public GlobalDeliveryDatePickerViewComponent(
            IDeliveryTimeService deliveryTimeService,
            IGlobalDeliveryTimeValidationService globalValidationService,
            IDateTimeHelper dateTimeHelper,
            IWorkContext workContext,
            IHttpContextAccessor httpContextAccessor,
            ICompanyService companyService)
        {
            _deliveryTimeService = deliveryTimeService;
            _globalValidationService = globalValidationService;
            _dateTimeHelper = dateTimeHelper;
            _workContext = workContext;
            _httpContextAccessor = httpContextAccessor;
            _companyService = companyService;
        }

        #endregion

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
            var model = new DeliveryDatePickerModel();

            try
            {
                // Get validation result first
                var validationResult = await _globalValidationService.ValidateCurrentSelectionAsync();
                
                // Populate validation info in model
                model.IsSelectionValid = validationResult.IsValid;
                model.ShouldShowPrompt = validationResult.ShouldPrompt;
                model.PromptType = validationResult.PromptType;
                model.PromptMessage = validationResult.PromptMessage ?? string.Empty;
                model.InvalidReason = validationResult.InvalidReason ?? string.Empty;

                // Get available delivery times
                var availableTimes = await _deliveryTimeService.GetAvailableDeliveryTimesAsync();
                model.MaxDaysAhead = await _deliveryTimeService.GetMaxDaysAheadAsync();

                // Set selected delivery time from validation result
                if (validationResult.IsValid && validationResult.SelectedDeliveryTime.HasValue)
                {
                    model.SelectedDeliveryTime = validationResult.SelectedDeliveryTime;
                    model.SelectedDeliveryTimeText = await FormatDeliveryTimeDisplayAsync(validationResult.SelectedDeliveryTime.Value);
                }

                // Convert times to display models using proper timezone conversion
                var currentCustomer = await _workContext.GetCurrentCustomerAsync();
                var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
                var timezoneInfo = company == null
                    ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                    : TZConvert.GetTimeZoneInfo(company.TimeZone);

                var now = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, timezoneInfo);
                var today = now.Date;
                var tomorrow = today.AddDays(1);

                foreach (var time in availableTimes)
                {
                    var deliveryTimeModel = new DeliveryTimeModel
                    {
                        DateTime = time,
                        DisplayText = FormatDeliveryTimeDisplay(time, now),
                        TimeDisplayText = time.ToString("h:mm tt", CultureInfo.InvariantCulture),
                        IsToday = time.Date == today,
                        IsTomorrow = time.Date == tomorrow
                    };

                    // Set date display text
                    if (deliveryTimeModel.IsToday)
                        deliveryTimeModel.DateDisplayText = "Today";
                    else if (deliveryTimeModel.IsTomorrow)
                        deliveryTimeModel.DateDisplayText = "Tomorrow";
                    else
                        deliveryTimeModel.DateDisplayText = time.ToString("dddd, MMM dd", CultureInfo.InvariantCulture);

                    model.AvailableDeliveryTimes.Add(deliveryTimeModel);
                }
            }
            catch
            {
                // If there's any error, show prompt for no selection
                model.ShouldShowPrompt = true;
                model.PromptType = DeliveryTimePromptType.NoSelection;
                model.PromptMessage = "Please select a delivery time";
            }

            return View("~/Plugins/Company.Company/Views/Shared/Components/GlobalDeliveryDatePicker/Default.cshtml", model);
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets the selected delivery time from session/cookie
        /// </summary>
        /// <returns>Selected delivery time or null</returns>
        private DateTime? GetSelectedDeliveryTime()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
                return null;

            // Try session first
            if (httpContext.Session.TryGetValue("SelectedDeliveryTime", out var sessionBytes))
            {
                var ticks = BitConverter.ToInt64(sessionBytes, 0);
                return new DateTime(ticks);
            }

            // Try cookie as fallback
            if (httpContext.Request.Cookies.TryGetValue("SelectedDeliveryTime", out var cookieValue) &&
                DateTime.TryParse(cookieValue, out var cookieDateTime))
            {
                return cookieDateTime;
            }

            return null;
        }

        /// <summary>
        /// Formats delivery time for display using current user timezone
        /// </summary>
        /// <param name="deliveryTime">Delivery time</param>
        /// <returns>Formatted display string</returns>
        private async Task<string> FormatDeliveryTimeDisplayAsync(DateTime deliveryTime)
        {
            // Get proper timezone conversion
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
            var timezoneInfo = company == null
                ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                : TZConvert.GetTimeZoneInfo(company.TimeZone);

            var now = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, timezoneInfo);
            return FormatDeliveryTimeDisplay(deliveryTime, now);
        }

        /// <summary>
        /// Formats delivery time for display using provided current time
        /// </summary>
        /// <param name="deliveryTime">Delivery time</param>
        /// <param name="now">Current time</param>
        /// <returns>Formatted display string</returns>
        private string FormatDeliveryTimeDisplay(DateTime deliveryTime, DateTime now)
        {
            var today = now.Date;
            var tomorrow = today.AddDays(1);

            string dateText;
            if (deliveryTime.Date == today)
                dateText = "Today";
            else if (deliveryTime.Date == tomorrow)
                dateText = "Tomorrow";
            else
                dateText = deliveryTime.ToString("dddd, MMM dd", CultureInfo.InvariantCulture);

            var timeText = deliveryTime.ToString("h:mm tt", CultureInfo.InvariantCulture);
            return $"{dateText} at {timeText}";
        }

        #endregion
    }
}
