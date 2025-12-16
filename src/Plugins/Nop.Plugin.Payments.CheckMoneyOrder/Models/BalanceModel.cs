using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.CheckMoneyOrder.Models
{
    public record BalanceModel : BaseNopModel
    {
        public string Balance { get; set; }
        public string RefreshCadence { get; set; }
    }
}
