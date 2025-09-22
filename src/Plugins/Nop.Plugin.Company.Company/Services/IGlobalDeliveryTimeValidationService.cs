using System;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Global delivery time validation service interface
    /// </summary>
    public partial interface IGlobalDeliveryTimeValidationService
    {
        /// <summary>
        /// Validates current delivery time selection and determines if prompt should be shown
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains validation result with prompt information
        /// </returns>
        Task<DeliveryTimeValidationResult> ValidateCurrentSelectionAsync();

        /// <summary>
        /// Gets the currently selected delivery time with validation
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the selected delivery time or null
        /// </returns>
        Task<DateTime?> GetValidatedSelectedDeliveryTimeAsync();

        /// <summary>
        /// Determines if delivery time selection should be prompted to the user
        /// </summary>
        /// <param name="currentPath">Current page path</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains true if prompt should be shown
        /// </returns>
        Task<bool> ShouldPromptForDeliveryTimeAsync(string currentPath);
    }

    /// <summary>
    /// Delivery time validation result
    /// </summary>
    public partial record DeliveryTimeValidationResult
    {
        /// <summary>
        /// Whether the current selection is valid
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Whether a prompt should be shown to user
        /// </summary>
        public bool ShouldPrompt { get; set; }

        /// <summary>
        /// The current selected delivery time (if valid)
        /// </summary>
        public DateTime? SelectedDeliveryTime { get; set; }

        /// <summary>
        /// Reason why selection is invalid (if any)
        /// </summary>
        public string InvalidReason { get; set; }

        /// <summary>
        /// Type of prompt to show
        /// </summary>
        public DeliveryTimePromptType PromptType { get; set; }

        /// <summary>
        /// Message to display in prompt
        /// </summary>
        public string PromptMessage { get; set; }
    }

    /// <summary>
    /// Delivery time prompt types
    /// </summary>
    public enum DeliveryTimePromptType
    {
        None,
        NoSelection,
        SelectionExpired,
        SelectionInvalid
    }
}

