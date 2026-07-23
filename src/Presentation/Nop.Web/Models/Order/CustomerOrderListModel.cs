using System;
using System.Collections.Generic;
using Nop.Core.Domain.Orders;
using Nop.Web.Framework.Models;
using static Nop.Web.Models.Order.OrderDetailsModel;

namespace Nop.Web.Models.Order
{
    public partial record CustomerOrderListModel : BaseNopModel
    {
        public CustomerOrderListModel()
        {
            Orders = new List<OrderDetailsModel>();
            RecurringOrders = new List<RecurringOrderModel>();
            RecurringPaymentErrors = new List<string>();
        }

        public IList<OrderDetailsModel> Orders { get; set; }
        public IList<RecurringOrderModel> RecurringOrders { get; set; }
        public IList<string> RecurringPaymentErrors { get; set; }

        #region Nested classes

        public partial record OrderDetailsModel : BaseNopEntityModel
        {
            public OrderDetailsModel()
            {
                Items = new List<OrderItemModel>();
            }
            public IList<OrderItemModel> Items { get; set; }
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
            public string DeliveryAddress { get; set; }
            /// <summary>
            /// True when this order is Pending payment via AmeriaVPos and still needs a
            /// card payment (self-pay checkout left it unpaid, e.g. InitPayment failed or
            /// the customer backed out before completing the hosted pay page) - drives the
            /// mobile Orders list's "Pay" button.
            /// </summary>
            public bool RequiresPayment { get; set; }
            /// <summary>
            /// The amount still owed by card when <see cref="RequiresPayment"/> is true (raw
            /// store-currency decimal, same convention as OrderItemModel.UnitPrice - the
            /// mobile app formats it client-side). Zero otherwise.
            /// </summary>
            public decimal AmountDue { get; set; }
        }

        public partial record RecurringOrderModel : BaseNopEntityModel
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