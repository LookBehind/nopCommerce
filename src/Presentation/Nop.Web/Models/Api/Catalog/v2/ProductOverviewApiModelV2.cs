using Nop.Web.Models.Api.Order;
using Nop.Web.Models.Media;

namespace Nop.Web.Models.Api.Catalog.v2
{
    public record ProductOverviewApiModelV2
    {
        public int Id { get; set; }
        public bool RibbonEnable { get; set; }
        public string RibbonText { get; set; }
        public string CategoryName { get; set; }
        public string Name { get; set; }
        public string ShortDescription { get; set; }
        public string FullDescription { get; set; }
        public string SeName { get; set; }
        public PictureModel[] Images { get; set; }
        public string Price { get; set; }
        public decimal PriceValue { get; set; }
        public int RatingSum { get; set; }
        public int TotalReviews { get; set; }
        public ProductAttributesApiModel ProductAttributesModel { get; set; }
        public VendorModel Vendor { get; set; }
    }

    public record ProductsOverviewApiModelV2(ProductOverviewApiModelV2[] Products);
}