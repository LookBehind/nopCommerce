using Nop.Core.Domain.Localization;

namespace Nop.Core.Domain.Companies
{
    public partial class Company : BaseEntity, ILocalizedEntity
    {
        public string Email { get; set; }
        public string Name { get; set; }
        public decimal AmountLimit { get; set; }
        public string TimeZone { get; set; }
        
        /// <summary>
        /// Gets or sets the number of days that customers of this company can order ahead of schedule
        /// </summary>
        public int OrderAheadDays { get; set; }
    }
}