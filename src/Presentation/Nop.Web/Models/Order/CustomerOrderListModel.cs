using System;
using System.Collections.Generic;
using Nop.Core.Domain.Orders;
using Nop.Web.Framework.Models;
using static Nop.Web.Models.Order.OrderDetailsModel;

namespace Nop.Web.Models.Order
{
    public record CustomerOrderListModel : BaseNopModel
    {
        public IList<OrderDetailsModel> Orders { get; set; } = new List<OrderDetailsModel>();
        public IList<RecurringOrderModel> RecurringOrders { get; set; } = new List<RecurringOrderModel>();
        public IList<string> RecurringPaymentErrors { get; set; } = new List<string>();

        #region Nested classes

        public record OrderDetailsModel : BaseNopEntityModel
        {
            /// TODO: remove after deprecating v1 OrderApiController
            /// This is left here for backward compatibility 
            public IList<OrderItemModel> Items { get; set; } = new List<OrderItemModel>();
            public DateTime ScheduleDate { get; set; }
            public int Rating { get; set; }
            public string RatingText { get; set; }
            public string CustomOrderNumber { get; set; }
            public string OrderTotal { get; set; }
            public bool IsReturnRequestAllowed { get; set; }
            public OrderStatus OrderStatusEnum { get; set; }
            public string OrderStatus { get; set; }
            public string PaymentStatus { get; set; }
            public string ShippingStatus { get; set; }
            public DateTime CreatedOn { get; set; }
        }

        public record RecurringOrderModel : BaseNopEntityModel
        {
            public string StartDate { get; set; }
            public string CycleInfo { get; set; }
            public string NextPayment { get; set; }
            public int TotalCycles { get; set; }
            public int CyclesRemaining { get; set; }
            public int InitialOrderId { get; set; }
            public bool CanRetryLastPayment { get; set; }
            public string InitialOrderNumber { get; set; }
            public bool CanCancel { get; set; }
        }

        #endregion
    }
}