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
        /// Gets or sets when the customer last viewed this case. Compared against
        /// LastStaffMessageUtc to compute "unread by customer" without joining the message
        /// table for every case in a list.
        /// </summary>
        public DateTime? CustomerLastReadUtc { get; set; }

        /// <summary>
        /// Gets or sets when staff last viewed this case (shared across all staff, same
        /// single-owner-workflow assumption as AssignedToCustomerId - not tracked per staff
        /// member). Compared against LastCustomerMessageUtc to compute "unread by staff".
        /// </summary>
        public DateTime? StaffLastReadUtc { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the most recent customer-authored activity - the
        /// case's own CreatedOnUtc initially (the Description counts as the customer's first
        /// "message"), then bumped on every customer SupportCaseMessage.
        /// </summary>
        public DateTime? LastCustomerMessageUtc { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the most recent staff-authored message, if any.
        /// Null until staff post their first reply.
        /// </summary>
        public DateTime? LastStaffMessageUtc { get; set; }

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

    /// <summary>
    /// Unread computation kept off the entity itself (a plain extension, not a property) -
    /// LINQ2DB maps entity properties by convention and there's no existing precedent in this
    /// codebase for a get-only computed property on a BaseEntity subclass; safer not to risk
    /// it being treated as a real column.
    /// </summary>
    public static class SupportCaseUnreadExtensions
    {
        /// <summary>
        /// Whether the case has staff activity (a reply) the customer hasn't seen yet. Status
        /// changes alone don't count - those are already communicated via push, this is
        /// scoped strictly to the message thread.
        /// </summary>
        public static bool IsUnreadByCustomer(this SupportCase supportCase) =>
            supportCase.LastStaffMessageUtc.HasValue &&
            (!supportCase.CustomerLastReadUtc.HasValue || supportCase.LastStaffMessageUtc > supportCase.CustomerLastReadUtc);

        /// <summary>
        /// Whether the case has customer activity (the initial submission, or a later reply)
        /// staff haven't seen yet.
        /// </summary>
        public static bool IsUnreadByStaff(this SupportCase supportCase) =>
            supportCase.LastCustomerMessageUtc.HasValue &&
            (!supportCase.StaffLastReadUtc.HasValue || supportCase.LastCustomerMessageUtc > supportCase.StaffLastReadUtc);
    }
}
