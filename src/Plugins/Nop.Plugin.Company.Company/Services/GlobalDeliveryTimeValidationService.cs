using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Companies;
using Nop.Services.Localization;
using TimeZoneConverter;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Global delivery time validation service implementation
    /// </summary>
    public partial class GlobalDeliveryTimeValidationService : IGlobalDeliveryTimeValidationService
    {
        #region Fields

        private readonly IDeliveryTimeService _deliveryTimeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWorkContext _workContext;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICompanyService _companyService;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public GlobalDeliveryTimeValidationService(
            IDeliveryTimeService deliveryTimeService,
            IHttpContextAccessor httpContextAccessor,
            IWorkContext workContext,
            IDateTimeHelper dateTimeHelper,
            ICompanyService companyService,
            ILocalizationService localizationService)
        {
            _deliveryTimeService = deliveryTimeService;
            _httpContextAccessor = httpContextAccessor;
            _workContext = workContext;
            _dateTimeHelper = dateTimeHelper;
            _companyService = companyService;
            _localizationService = localizationService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Validates current delivery time selection and determines if prompt should be shown
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains validation result with prompt information
        /// </returns>
        public virtual async Task<DeliveryTimeValidationResult> ValidateCurrentSelectionAsync()
        {
            var result = new DeliveryTimeValidationResult
            {
                IsValid = false,
                ShouldPrompt = false,
                PromptType = DeliveryTimePromptType.None
            };

            try
            {
                // Get selected delivery time
                var selectedTime = await GetSelectedDeliveryTimeAsync();

                // If no selection, prompt for selection
                if (!selectedTime.HasValue)
                {
                    result.ShouldPrompt = true;
                    result.PromptType = DeliveryTimePromptType.NoSelection;
                    result.PromptMessage = await _localizationService.GetResourceAsync("DeliveryTime.Prompt.NoSelection");
                    return result;
                }

                // Validate the selected time is still available
                var isAvailable = await _deliveryTimeService.IsDeliveryTimeAvailableAsync(selectedTime.Value);

                if (!isAvailable)
                {
                    // Check if it's expired (past) vs just invalid
                    var currentCustomer = await _workContext.GetCurrentCustomerAsync();
                    var company = await _companyService.GetCompanyByCustomerIdAsync(currentCustomer.Id);
                    var timezoneInfo = company == null
                        ? await _dateTimeHelper.GetCustomerTimeZoneAsync(currentCustomer)
                        : TZConvert.GetTimeZoneInfo(company.TimeZone);

                    var now = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, timezoneInfo);

                    if (selectedTime.Value < now)
                    {
                        result.ShouldPrompt = true;
                        result.PromptType = DeliveryTimePromptType.SelectionExpired;
                        result.InvalidReason = "Selected delivery time has passed";
                        result.PromptMessage = await _localizationService.GetResourceAsync("DeliveryTime.Prompt.SelectionExpired");
                        
                        // Clear invalid selection
                        await ClearSelectedDeliveryTimeAsync();
                    }
                    else
                    {
                        result.ShouldPrompt = true;
                        result.PromptType = DeliveryTimePromptType.SelectionInvalid;
                        result.InvalidReason = "Selected delivery time is no longer available";
                        result.PromptMessage = await _localizationService.GetResourceAsync("DeliveryTime.Prompt.SelectionInvalid");
                        
                        // Clear invalid selection
                        await ClearSelectedDeliveryTimeAsync();
                    }
                    
                    return result;
                }

                // Selection is valid
                result.IsValid = true;
                result.SelectedDeliveryTime = selectedTime.Value;
                return result;
            }
            catch (Exception)
            {
                // On error, assume no valid selection and prompt
                result.ShouldPrompt = true;
                result.PromptType = DeliveryTimePromptType.NoSelection;
                result.PromptMessage = await _localizationService.GetResourceAsync("DeliveryTime.Prompt.NoSelection");
                return result;
            }
        }

        /// <summary>
        /// Gets the currently selected delivery time with validation
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the selected delivery time or null
        /// </returns>
        public virtual async Task<DateTime?> GetValidatedSelectedDeliveryTimeAsync()
        {
            var validation = await ValidateCurrentSelectionAsync();
            return validation.IsValid ? validation.SelectedDeliveryTime : null;
        }

        /// <summary>
        /// Determines if delivery time selection should be prompted to the user
        /// </summary>
        /// <param name="currentPath">Current page path</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains true if prompt should be shown
        /// </returns>
        public virtual async Task<bool> ShouldPromptForDeliveryTimeAsync(string currentPath)
        {
            // Don't prompt on admin pages
            if (currentPath?.Contains("/Admin/") == true)
                return false;

            // Don't prompt on login/register pages  
            if (currentPath?.Contains("/login") == true || currentPath?.Contains("/register") == true)
                return false;

            // Always check validation and return whether we should prompt
            var validation = await ValidateCurrentSelectionAsync();
            return validation.ShouldPrompt;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets the selected delivery time from session/cookie
        /// </summary>
        /// <returns>Selected delivery time or null</returns>
        private async Task<DateTime?> GetSelectedDeliveryTimeAsync()
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
        /// Clears the selected delivery time from session and cookie
        /// </summary>
        private async Task ClearSelectedDeliveryTimeAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
                return;

            httpContext.Session.Remove("SelectedDeliveryTime");
            httpContext.Response.Cookies.Delete("SelectedDeliveryTime");
        }

        #endregion
    }
}

