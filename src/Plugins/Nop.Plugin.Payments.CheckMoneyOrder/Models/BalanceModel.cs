using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.CheckMoneyOrder.Models
{
    public record BalanceModel : BaseNopModel
    {
        public bool HasBalance { get; set; }
        public decimal TotalBalance { get; set; }
        public decimal RemainingBalance { get; set; }
        public string RemainingBalanceFormatted { get; set; }
        public decimal UsedBalance { get; set; }
        public string UsedBalanceFormatted { get; set; }
        public decimal RecommendedSpending { get; set; }
        public string RecommendedSpendingFormatted { get; set; }
    }
}
