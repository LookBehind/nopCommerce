using System;

namespace Nop.Core.Domain.Companies
{
    /// <summary>
    /// Represents a per-date day-off override for a vendor under a company. One row per
    /// (CompanyId, VendorId, Date). Restoring a day flips IsOff back to false and stamps
    /// RestoredOnUtc rather than deleting the row, so there's a visible history.
    /// </summary>
    public partial class CompanyVendorDayOff : BaseEntity
    {
        /// <summary>
        /// Gets or sets the company identifier
        /// </summary>
        public int CompanyId { get; set; }

        /// <summary>
        /// Gets or sets the vendor identifier
        /// </summary>
        public int VendorId { get; set; }

        /// <summary>
        /// Gets or sets the company-local calendar date (time component zeroed)
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the vendor is currently off on this date
        /// </summary>
        public bool IsOff { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) the day was marked off
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) the day was restored, if it has been
        /// </summary>
        public DateTime? RestoredOnUtc { get; set; }
    }
}
