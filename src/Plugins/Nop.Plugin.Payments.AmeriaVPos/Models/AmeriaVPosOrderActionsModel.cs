using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.AmeriaVPos.Models
{
    public record AmeriaVPosOrderActionsModel : BaseNopModel
    {
        public int OrderId { get; set; }

        public decimal ChargedAmount { get; set; }
    }
}
