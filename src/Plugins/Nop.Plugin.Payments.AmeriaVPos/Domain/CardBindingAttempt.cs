using System;
using Nop.Core;

namespace Nop.Plugin.Payments.AmeriaVPos.Domain
{
    /// <summary>
    /// Status of a card-binding verification attempt (the one-time real charge that
    /// creates a binding - see the vPOS API doc, there is no $0 verify-only call).
    /// </summary>
    public enum CardBindingAttemptStatus
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
        /// Verification charge confirmed paid via GetPaymentDetails, binding created and
        /// the verification amount refunded
        /// </summary>
        Bound = 20,

        /// <summary>
        /// Confirmed declined via GetPaymentDetails - no binding created
        /// </summary>
        Declined = 30
    }

    /// <summary>
    /// Represents one "add a card" attempt - a real, nominal verification charge
    /// (AmeriaVPosSettings.CardVerificationAmount) made with a fresh CardHolderID to
    /// create a binding, immediately refunded on success. Deliberately separate from
    /// AmeriaVPosPaymentAttempt (which is tightly OrderId-scoped and drives real order
    /// payment/reconciliation flows) - this table tracks a different kind of
    /// transaction with no Order behind it at all.
    /// </summary>
    public partial class CardBindingAttempt : BaseEntity
    {
        public int CustomerId { get; set; }

        /// <summary>
        /// The freshly generated CardHolderID this attempt tries to bind. Persisted to
        /// CustomerBoundCard only once the attempt resolves to Bound.
        /// </summary>
        public string CardHolderId { get; set; }

        /// <summary>
        /// Gets or sets the AmeriaBank-facing PaymentID returned by InitPayment
        /// </summary>
        public string PaymentId { get; set; }

        public decimal VerificationAmount { get; set; }

        public int StatusId { get; set; }

        public CardBindingAttemptStatus Status
        {
            get => (CardBindingAttemptStatus)StatusId;
            set => StatusId = (int)value;
        }

        public DateTime CreatedOnUtc { get; set; }

        public DateTime? ResolvedOnUtc { get; set; }
    }
}
