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
}
