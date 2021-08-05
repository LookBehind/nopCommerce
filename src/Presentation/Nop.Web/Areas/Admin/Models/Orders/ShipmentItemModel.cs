using System.Collections.Generic;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Web.Areas.Admin.Models.Orders
{
    /// <summary>
    /// Represents a shipment item model
    /// </summary>
    public partial record ShipmentItemModel : BaseNopEntityModel
    {
        #region Ctor

        public ShipmentItemModel()
        {
            AvailableWarehouses = new List<WarehouseInfo>();
        }

        #endregion

        #region Properties

        public int OrderItemId { get; set; }

        public int ProductId { get; set; }

        [NopResourceDisplayName("Admin.Orders.Shipments.Products.ProductName")]
        public string ProductName { get; set; }

        public string Sku { get; set; }

        public string AttributeInfo { get; set; }

        public string RentalInfo { get; set; }

        public bool ShipSeparately { get; set; }

        //weight of one item (product)
        [NopResourceDisplayName("Admin.Orders.Shipments.Products.ItemWeight")]
        public string ItemWeight { get; set; }

        [NopResourceDisplayName("Admin.Orders.Shipments.Products.ItemDimensions")]
        public string ItemDimensions { get; set; }

        public decimal QuantityToAdd { get; set; }

        public decimal QuantityOrdered { get; set; }

        [NopResourceDisplayName("Admin.Orders.Shipments.Products.QtyShipped")]
        public decimal QuantityInThisShipment { get; set; }

        public decimal QuantityInAllShipments { get; set; }


        public string ShippedFromWarehouse { get; set; }

        public bool AllowToChooseWarehouse { get; set; }

        //used before a shipment is created
        public List<WarehouseInfo> AvailableWarehouses { get; set; }

        #endregion

        #region Nested Classes

        public record WarehouseInfo : BaseNopModel
        {
            public int WarehouseId { get; set; }
            public string WarehouseName { get; set; }
            public decimal StockQuantity { get; set; }
            public decimal ReservedQuantity { get; set; }
            public int PlannedQuantity { get; set; }
        }

        #endregion
    }
}