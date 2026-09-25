using System;
using Nop.Core;

namespace Nop.Plugin.Company.Support.Domain
{
    /// <summary>
    /// One row per status a SupportCase has ever entered (including its initial Pending row,
    /// inserted at case creation). The admin case-detail page renders this as a timeline with
    /// a computed duration per row (next row's EnteredOnUtc, or now for the last row, minus
    /// this row's EnteredOnUtc) - no stored duration column, so it's never stale.
    /// </summary>
    public partial class SupportCaseStatusHistory : BaseEntity
    {
        /// <summary>
        /// Gets or sets the support case identifier
        /// </summary>
        public int SupportCaseId { get; set; }

        /// <summary>
        /// Gets or sets the status identifier (int cast of SupportCaseStatus)
        /// </summary>
        public int StatusId { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) the case entered this status
        /// </summary>
        public DateTime EnteredOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the staff customer identifier who made this change, if any (null for
        /// the initial Pending row created by the customer's own submission)
        /// </summary>
        public int? ChangedByCustomerId { get; set; }

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
