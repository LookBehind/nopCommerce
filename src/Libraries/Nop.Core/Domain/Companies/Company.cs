using Nop.Core.Domain.Localization;

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
        public AmountLimitType AmountLimitType { get; set; }
        public string TimeZone { get; set; }
    }
}