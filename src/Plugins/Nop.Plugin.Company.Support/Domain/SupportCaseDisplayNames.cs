using System.Collections.Generic;

namespace Nop.Plugin.Company.Support.Domain
{
    /// <summary>
    /// Plain (non-localized) staff-facing display strings for the two enums above - the admin
    /// queue is internal-only, so this skips the full LocaleStringResource seeding ceremony
    /// used for the field labels around it (see AddSupportLocalesMigration).
    /// </summary>
    public static class SupportCaseDisplayNames
    {
        public static readonly IReadOnlyDictionary<SupportCaseCategory, string> Category = new Dictionary<SupportCaseCategory, string>
        {
            [SupportCaseCategory.Technical] = "Technical",
            [SupportCaseCategory.VendorQuality] = "Vendor Quality",
            [SupportCaseCategory.NewVendorRequest] = "New Vendor Request",
            [SupportCaseCategory.DeliveryInquiry] = "Delivery Inquiry",
            [SupportCaseCategory.BillingInquiry] = "Billing Inquiry"
        };

        public static readonly IReadOnlyDictionary<SupportCaseStatus, string> Status = new Dictionary<SupportCaseStatus, string>
        {
            [SupportCaseStatus.Pending] = "Pending",
            [SupportCaseStatus.InProgress] = "In progress",
            [SupportCaseStatus.Resolved] = "Resolved"
        };
    }
}
