using System;
using System.Collections.Generic;
using Nop.Web.Models.Order;

namespace Nop.Web.Models.Api.Order
{
    public record OrderItemDetails
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string CategoryName { get; set; }
        public int Quantity { get; set; }
        public string ImageUrl { get; set; }
        public string Price { get; set; }
        public decimal PriceValue { get; set; }
        public VendorModel Vendor { get; set; }
        public int RatingSum { get; set; }
        public int TotalReviews { get; set; }
        public string AttributeDescription { get; set; }
    }
    
    public record OrderDetailsModel : CustomerOrderListModel.OrderDetailsModel
    {
        public IReadOnlyList<OrderItemDetails> OrderItems { get; set; }
    }
    
    public record OrdersModel
    {
        public IReadOnlyList<OrderDetailsModel> Orders { get; set; } = 
            new List<OrderDetailsModel>();
    }
}