using Nop.Web.Models.Api.Catalog;

namespace Nop.Web.Models.Api.Order
{
    public record AddToCartModel()
    {
        public string ScheduleDate { get; set; }
        public int ProductId { get; set; }
        public ProductOrderSimpleAttribute[] ProductAttributes { get; set; }
    }
}