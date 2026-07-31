using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Stores;

namespace Nop.Core.Domain.Companies
{
    public enum AmountLimitType
    {
        Daily,
        Weekly,
        Monthly
    }
    
    public partial class Company : BaseEntity, ILocalizedEntity
    {
        public string Email { get; set; }
        public string Name { get; set; }
        public decimal AmountLimit { get; set; }
        public int AmountLimitTypeId { get; set; }

        public AmountLimitType AmountLimitType
        {
            get => (AmountLimitType)AmountLimitTypeId; 
            set => AmountLimitTypeId = (int)value;
        }
        public string TimeZone { get; set; }

        /// <summary>
        /// Gets or sets the number of days that customers of this company can order ahead of schedule
        /// </summary>
        public int OrderAheadDays { get; set; }

        /// <summary>
        /// Gets or sets the store this company belongs to. Today MySnacks is one store per tenant
        /// (so every company in a tenant's DB points at that same store); this makes that relationship
        /// a real FK ahead of a planned move to a single deployment serving multiple tenants/stores.
        /// </summary>
        public int StoreId { get; set; }
    }
}