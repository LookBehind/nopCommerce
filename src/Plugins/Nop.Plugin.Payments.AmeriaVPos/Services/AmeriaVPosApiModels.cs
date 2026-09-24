namespace Nop.Plugin.Payments.AmeriaVPos.Services
{
    public class InitPaymentRequest
    {
        public string ClientID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public int OrderID { get; set; }
        public decimal Amount { get; set; }
        public string BackURL { get; set; }

        /// <summary>
        /// Set only when this InitPayment should create a card binding (a fresh,
        /// merchant-generated unique id - see the vPOS API doc's "Binding Transactions"
        /// section). Left null for an ordinary one-time order payment.
        /// </summary>
        public string CardHolderID { get; set; }
    }

    public class InitPaymentResponse
    {
        public string PaymentID { get; set; }
        public int ResponseCode { get; set; }
        public string ResponseMessage { get; set; }
    }

    public class PaymentDetailsRequest
    {
        public string PaymentID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class PaymentDetailsResponse
    {
        public decimal Amount { get; set; }
        public decimal DepositedAmount { get; set; }
        public decimal RefundedAmount { get; set; }
        public string OrderID { get; set; }
        public string PaymentState { get; set; }
        public string OrderStatus { get; set; }
        public string ResponseCode { get; set; }
        public string Rrn { get; set; }

        /// <summary>
        /// Present when this payment was made with a CardHolderID set on InitPayment -
        /// confirms which binding this transaction created/used.
        /// </summary>
        public string CardHolderID { get; set; }

        /// <summary>
        /// AmeriaBank's own binding identifier - informational only, MakeBindingPayment/
        /// DeactivateBinding key off CardHolderID, not this.
        /// </summary>
        public string BindingID { get; set; }

        /// <summary>
        /// Masked card number (e.g. "428895******1234"), present once a binding was created
        /// </summary>
        public string CardNumber { get; set; }

        public string ExpDate { get; set; }
    }

    public class RefundPaymentRequest
    {
        public string PaymentID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public decimal Amount { get; set; }
    }

    public class CancelPaymentRequest
    {
        public string PaymentID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// Shared response shape for RefundPayment/CancelPayment - both only carry a response code/message
    /// </summary>
    public class VPosActionResponse
    {
        public string ResponseCode { get; set; }
        public string ResponseMessage { get; set; }
    }

    /// <summary>
    /// PaymentsEnum value for a binding transaction, per the vPOS API doc's
    /// MakeBindingPayment/GetBindings/DeactivateBinding/ActivateBinding request shapes
    /// (5 = MainRest/arca, 7 = PayPal, 6 = Binding).
    /// </summary>
    public static class VPosPaymentType
    {
        public const int Binding = 6;
    }

    /// <summary>
    /// Charges a previously-bound card directly - no redirect, resolves synchronously
    /// (unlike InitPayment, which only creates a binding via a real hosted-page charge).
    /// </summary>
    public class MakeBindingPaymentRequest
    {
        public string ClientID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string CardHolderID { get; set; }
        public decimal Amount { get; set; }
        public int OrderID { get; set; }
        public string BackURL { get; set; }
        public int PaymentType { get; set; } = VPosPaymentType.Binding;
        public string Description { get; set; }
        public string Currency { get; set; }
    }

    public class MakeBindingPaymentResponse
    {
        public string PaymentID { get; set; }
        public string ResponseCode { get; set; }
        public decimal Amount { get; set; }
        public decimal DepositedAmount { get; set; }
        public string CardNumber { get; set; }
        public string OrderID { get; set; }
        public string PaymentState { get; set; }
        public string OrderStatus { get; set; }
        public string Rrn { get; set; }
        public string CardHolderID { get; set; }
        public string BindingID { get; set; }
    }

    public class DeactivateBindingRequest
    {
        public string ClientID { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string CardHolderID { get; set; }
        public int PaymentType { get; set; } = VPosPaymentType.Binding;
    }

    public class DeactivateBindingResponse
    {
        public string ResponseCode { get; set; }
        public string ResponseMessage { get; set; }
        public string CardHolderID { get; set; }
    }
}
