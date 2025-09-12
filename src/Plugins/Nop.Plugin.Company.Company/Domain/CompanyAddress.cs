using Nop.Core;

namespace Nop.Plugin.Company.Company.Domain
{
    /// <summary>
    /// Represents a company address mapping
    /// </summary>
    public partial class CompanyAddress : BaseEntity
    {
        /// <summary>
        /// Gets or sets the company identifier
        /// </summary>
        public int CompanyId { get; set; }
        
        /// <summary>
        /// Gets or sets the address identifier
        /// </summary>
        public int AddressId { get; set; }
    }
}
