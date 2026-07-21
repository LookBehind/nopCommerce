using System;
using Nop.Core;

namespace Nop.Plugin.Payments.AmeriaVPos.Domain
{
    /// <summary>
    /// Status of an AmeriaVPos payment attempt
    /// </summary>
    public enum AmeriaVPosPaymentAttemptStatus
    {
        /// <summary>
        /// InitPayment has not been called yet (row just created to reserve a VposOrderId)
        /// </summary>
        Started = 0,

        /// <summary>
        /// InitPayment succeeded; customer has been sent to the hosted pay page
        /// </summary>
        Redirected = 10,

        /// <summary>
        /// Confirmed paid via GetPaymentDetails
        /// </summary>
        Paid = 20,

        /// <summary>
        /// Confirmed declined via GetPaymentDetails
        /// </summary>
        Declined = 30,

        /// <summary>
        /// Refunded (fully or partially) via the admin refund action
        /// </summary>
        Refunded = 40,

        /// <summary>
        /// Cancelled/voided via the admin cancel action
        /// </summary>
        Cancelled = 50,

        /// <summary>
        /// Never resolved within the reconciliation window - treated as abandoned
        /// </summary>
        Abandoned = 60
    }

    /// <summary>
    /// Represents one AmeriaBank vPOS InitPayment attempt for an order. A single order
    /// may have multiple attempts (retries after a decline/abandonment), each with its
    /// own AmeriaBank-facing OrderID (this row's own Id, decoupled from Order.Id -
    /// AmeriaBank rejects a repeat InitPayment against the same OrderID).
    /// </summary>
    public partial class AmeriaVPosPaymentAttempt : BaseEntity
    {
        /// <summary>
        /// Gets or sets the nopCommerce order identifier this attempt belongs to
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// Gets or sets the 1-based attempt number for this order (1 = first attempt)
        /// </summary>
        public int AttemptNumber { get; set; }

        /// <summary>
        /// Gets or sets the AmeriaBank-facing PaymentID returned by InitPayment
        /// </summary>
        public string PaymentId { get; set; }

        /// <summary>
        /// Gets or sets the amount requested from AmeriaBank for this attempt (the
        /// allowance shortfall, which may be less than the order total)
        /// </summary>
        public decimal RequestedAmount { get; set; }

        /// <summary>
        /// Gets or sets the amount AmeriaBank confirmed as actually charged, once resolved
        /// </summary>
        public decimal? ChargedAmount { get; set; }

        /// <summary>
        /// Gets or sets the AmeriaBank RRN (transaction reference) once resolved
        /// </summary>
        public string Rrn { get; set; }

        /// <summary>
        /// Gets or sets which surface initiated this attempt ("Web" or "Mobile") - decides
        /// how AmeriaVPosController.BackUrlReturn redirects once resolved (nopCommerce's
        /// CheckoutCompleted page for web, the mysnacks:// deep link for mobile, since a
        /// mobile order has no OPC/cart session for CheckoutCompleted to render against).
        /// </summary>
        public string Platform { get; set; }

        public int StatusId { get; set; }

        public AmeriaVPosPaymentAttemptStatus Status
        {
            get => (AmeriaVPosPaymentAttemptStatus)StatusId;
            set => StatusId = (int)value;
        }

        public DateTime CreatedOnUtc { get; set; }

        public DateTime? ResolvedOnUtc { get; set; }
    }
}
