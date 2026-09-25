using System;
using Nop.Core;

namespace Nop.Plugin.Company.Support.Domain
{
    /// <summary>
    /// A customer-submitted support inquiry. Status transitions are also recorded, one row
    /// per entered status, in SupportCaseStatusHistory - that table (not this entity) is
    /// the source of truth for "how long was this case in status X", computed as the gap
    /// between one history row's EnteredOnUtc and the next (or now, for the current status).
    /// </summary>
    public partial class SupportCase : BaseEntity
    {
        /// <summary>
        /// Gets or sets the customer identifier who submitted this case
        /// </summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// Gets or sets the store identifier (multi-tenant scoping, same as every other
        /// customer-facing entity in this codebase)
        /// </summary>
        public int StoreId { get; set; }

        /// <summary>
        /// Gets or sets the category identifier (int cast of SupportCaseCategory)
        /// </summary>
        public int CategoryId { get; set; }

        /// <summary>
        /// Gets or sets the vendor identifier - required (non-null) when
        /// Category == VendorQuality, always null otherwise. Enforced client-side (the
        /// mobile create form) and by the api/v2/support endpoint, not a DB constraint.
        /// </summary>
        public int? VendorId { get; set; }

        /// <summary>
        /// Gets or sets a short subject line
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Gets or sets the full inquiry description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the status identifier (int cast of SupportCaseStatus)
        /// </summary>
        public int StatusId { get; set; }

        /// <summary>
        /// Gets or sets the staff customer identifier who has self-assigned this case, if any
        /// </summary>
        public int? AssignedToCustomerId { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) the case was created
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) of the most recent status change
        /// (denormalized copy of the latest SupportCaseStatusHistory row, for cheap sorting/
        /// display without a join - the history table stays the source of truth for duration)
        /// </summary>
        public DateTime UpdatedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the category
        /// </summary>
        public SupportCaseCategory Category
        {
            get => (SupportCaseCategory)CategoryId;
            set => CategoryId = (int)value;
        }

        /// <summary>
        /// Gets or sets the status
        /// </summary>
        public SupportCaseStatus Status
        {
            get => (SupportCaseStatus)StatusId;
            set => StatusId = (int)value;
        }
    }
}
