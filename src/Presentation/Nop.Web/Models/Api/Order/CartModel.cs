using System.Collections.Generic;
using Nop.Web.Models.Api.Catalog;
using Nop.Web.Models.Catalog;

namespace Nop.Web.Models.Api.Order
{
    public record VendorModel()
    {
        public string Name { get; set; }
        public string PictureUrl { get; set; }
    }

    public record ShoppingCartItemModel()
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int ShoppingCartItemId { get; set; }
        public string Name { get; set; }
        public string CategoryName { get; set; }
        public VendorModel Vendor { get; set; }
        public string ImageUrl { get; set; }
        public string Price { get; set; }
        public decimal PriceValue { get; set; }
        public int RatingSum { get; set; }
        public int TotalReviews { get; set; }
        public int Quantity { get; set; }
        public ProductOrderSimpleAttribute[] ProductAttributes { get; set; }
    }

    public record CartModel()
    {
        public IReadOnlyList<ShoppingCartItemModel> Items { get; set; }
        public string CartTotal { get; set; }
    }
}