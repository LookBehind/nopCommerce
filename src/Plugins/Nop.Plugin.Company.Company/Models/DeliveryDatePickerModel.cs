using System;
using System.Collections.Generic;
using Nop.Web.Framework.Models;
using Nop.Plugin.Company.Company.Services;

namespace Nop.Plugin.Company.Company.Models
{
    /// <summary>
    /// Delivery date picker model
    /// </summary>
    public partial record DeliveryDatePickerModel : BaseNopModel
    {
        public DeliveryDatePickerModel()
        {
            AvailableDeliveryTimes = new List<DeliveryTimeModel>();
        }

        /// <summary>
        /// Currently selected delivery time
        /// </summary>
        public DateTime? SelectedDeliveryTime { get; set; }

        /// <summary>
        /// Display text for selected delivery time
        /// </summary>
        public string SelectedDeliveryTimeText { get; set; }

        /// <summary>
        /// Available delivery times grouped by date
        /// </summary>
        public IList<DeliveryTimeModel> AvailableDeliveryTimes { get; set; }

        /// <summary>
        /// Maximum days ahead allowed for ordering
        /// </summary>
        public int MaxDaysAhead { get; set; }

        /// <summary>
        /// Whether the user has already selected a delivery time
        /// </summary>
        public bool HasSelectedTime => SelectedDeliveryTime.HasValue;

        /// <summary>
        /// Whether the current selection is valid
        /// </summary>
        public bool IsSelectionValid { get; set; } = true;

        /// <summary>
        /// Whether to show a prompt to the user
        /// </summary>
        public bool ShouldShowPrompt { get; set; }

        /// <summary>
        /// Type of prompt to display
        /// </summary>
        public DeliveryTimePromptType PromptType { get; set; } = DeliveryTimePromptType.None;

        /// <summary>
        /// Message to display in the prompt
        /// </summary>
        public string PromptMessage { get; set; }

        /// <summary>
        /// Reason why selection is invalid (if any)
        /// </summary>
        public string InvalidReason { get; set; }

        /// <summary>
        /// Whether to show the picker as urgent/highlighted
        /// </summary>
        public bool IsUrgent => ShouldShowPrompt && (PromptType == DeliveryTimePromptType.SelectionExpired || PromptType == DeliveryTimePromptType.SelectionInvalid);

        /// <summary>
        /// CSS class for the picker state
        /// </summary>
        public string StateClass => ShouldShowPrompt switch
        {
            true when PromptType == DeliveryTimePromptType.NoSelection => "no-selection",
            true when PromptType == DeliveryTimePromptType.SelectionExpired => "selection-expired",
            true when PromptType == DeliveryTimePromptType.SelectionInvalid => "selection-invalid",
            _ => HasSelectedTime ? "has-selection" : "no-selection"
        };
    }

    /// <summary>
    /// Delivery time model for the picker
    /// </summary>
    public partial record DeliveryTimeModel
    {
        /// <summary>
        /// Delivery date and time
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// Display text for the time slot
        /// </summary>
        public string DisplayText { get; set; }

        /// <summary>
        /// Date display text (e.g., "Today", "Tomorrow", "Wednesday, Jan 15")
        /// </summary>
        public string DateDisplayText { get; set; }

        /// <summary>
        /// Time display text (e.g., "1:00 PM")
        /// </summary>
        public string TimeDisplayText { get; set; }

        /// <summary>
        /// Whether this time slot is for today
        /// </summary>
        public bool IsToday { get; set; }

        /// <summary>
        /// Whether this time slot is for tomorrow
        /// </summary>
        public bool IsTomorrow { get; set; }
    }
}

