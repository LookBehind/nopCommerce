namespace Nop.Core.Domain.Companies
{
    /// <summary>
    /// Represents a working day of week for a vendor under a company. Any row for a
    /// given (CompanyId, VendorId) pair restricts that pair to only the recorded days;
    /// no rows at all for the pair means the vendor works every day (the default).
    /// </summary>
    public partial class CompanyVendorWorkingDay : BaseEntity
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
        /// Gets or sets the day of week (System.DayOfWeek numeric value, 0=Sunday..6=Saturday)
        /// </summary>
        public int DayOfWeekId { get; set; }
    }
}
