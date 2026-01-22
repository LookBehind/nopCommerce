using System;
using System.Threading.Tasks;
using Nop.Core;
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
        private readonly IDeliveryTimeStorageService _deliveryTimeStorageService;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICompanyService _companyService;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public GlobalDeliveryTimeValidationService(
            IDeliveryTimeService deliveryTimeService,
            IDeliveryTimeStorageService deliveryTimeStorageService,
            IWorkContext workContext,
            IStoreContext storeContext,
            IDateTimeHelper dateTimeHelper,
            ICompanyService companyService,
            ILocalizationService localizationService)
        {
            _deliveryTimeService = deliveryTimeService;
            _deliveryTimeStorageService = deliveryTimeStorageService;
            _workContext = workContext;
            _storeContext = storeContext;
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
                    result.ShouldPrompt = true;
                    result.PromptType = DeliveryTimePromptType.SelectionInvalid;
                    result.InvalidReason = "Selected delivery time is no longer available";
                    result.PromptMessage = await _localizationService.GetResourceAsync("DeliveryTime.Prompt.SelectionInvalid");

                    // Clear invalid selection
                    var currentCustomer = await _workContext.GetCurrentCustomerAsync();
                    var currentStore = await _storeContext.GetCurrentStoreAsync();
                    await _deliveryTimeStorageService.ClearSelectedDeliveryTimeAsync(currentCustomer, currentStore.Id);
                    
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
        /// Gets the selected delivery time from generic attribute service
        /// </summary>
        /// <returns>Selected delivery time or null</returns>
        private async Task<DateTime?> GetSelectedDeliveryTimeAsync()
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var currentStore = await _storeContext.GetCurrentStoreAsync();
            return await _deliveryTimeStorageService.GetSelectedDeliveryTimeAsync(currentCustomer, currentStore.Id);
        }

        #endregion
    }
}

