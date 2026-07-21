using System.Threading.Tasks;
using Nop.Core.Domain.Orders;

namespace Nop.Services.Payments;

/// <summary>
/// Core AmeriaBank vPOS payment logic, shared between the web IPaymentMethod flow
/// (Nop.Plugin.Payments.AmeriaVPos) and the mobile order-confirmation API
/// (which needs the same allowance-vs-total decision but a JSON response instead
/// of an HTTP redirect).
/// </summary>
public interface IAmeriaVPosPaymentService
{
    /// <summary>
    /// Resolves whether <paramref name="order"/> is fully covered by the customer's
    /// company allowance (marks it Paid directly, no card involved) or needs a card
    /// payment for the shortfall (creates a payment attempt and returns a redirect URL).
    /// </summary>
    /// <param name="order">The just-placed order (Pending)</param>
    /// <param name="platform">"Web" or "Mobile" - decides how BackUrlReturn redirects
    /// once the attempt resolves (a mobile order has no OPC/cart session for the web
    /// CheckoutCompleted page to render against, so it needs the mysnacks:// deep link
    /// instead)</param>
    Task<AmeriaVPosPaymentResult> InitiateOrCompletePaymentAsync(Order order, string platform = "Web");

    /// <summary>
    /// Pulls the authoritative status for the order's latest payment attempt from
    /// AmeriaBank and updates the order/attempt accordingly. Used by the BackURL
    /// return action and the abandoned-session reconciliation task - never trust a
    /// redirect/deep-link query string instead of calling this.
    /// </summary>
    Task<AmeriaVPosPaymentResult> ResolvePaymentAsync(Order order);

    /// <summary>
    /// Reads the order's latest payment attempt status from the local ledger only - no
    /// live AmeriaBank call. Used by the mobile deep-link return screen's status poll,
    /// which should not hammer AmeriaBank on every check; BackURL return and the
    /// reconciliation task are what keep the ledger itself up to date via ResolvePaymentAsync.
    /// </summary>
    Task<AmeriaVPosPaymentResult> GetLatestAttemptStatusAsync(Order order);

    /// <summary>
    /// Refunds (fully or partially) the order's completed payment attempt via AmeriaBank
    /// and updates the order/attempt on success. Intentionally not exposed through the
    /// standard IPaymentMethod.RefundAsync - callers must go through a distinct,
    /// explicitly-confirmed admin action.
    /// </summary>
    Task<bool> RefundAsync(Order order, decimal amount);

    /// <summary>
    /// Cancels the order's completed payment attempt via AmeriaBank and updates the
    /// order/attempt on success. Intentionally not exposed through the standard
    /// IPaymentMethod.VoidAsync - see RefundAsync.
    /// </summary>
    Task<bool> CancelAsync(Order order);
}

public class AmeriaVPosPaymentResult
{
    /// <summary>
    /// True if the order needs a card payment (fully or partially) and the customer
    /// should be sent to <see cref="PaymentUrl"/>. False if it was fully covered by
    /// allowance (already marked Paid) or has already resolved.
    /// </summary>
    public bool RequiresPayment { get; set; }

    /// <summary>
    /// The AmeriaBank hosted pay-page URL to redirect/open, when RequiresPayment is true
    /// and no attempt has resolved yet.
    /// </summary>
    public string PaymentUrl { get; set; }

    /// <summary>
    /// The portion of the order total the customer must pay by card (the allowance
    /// shortfall). Zero when the order is fully covered by allowance.
    /// </summary>
    public decimal AmountDue { get; set; }

    /// <summary>
    /// The portion of the order total covered by the customer's company allowance.
    /// Zero for a fully allowance-exempt customer; equal to OrderTotal when fully covered.
    /// </summary>
    public decimal AmountCoveredByAllowance { get; set; }

    /// <summary>
    /// The resolved attempt status ("Paid", "Declined", "Refunded", "Cancelled",
    /// "Redirected", "Abandoned") after ResolvePaymentAsync, or null before resolution.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// True once ResolvePaymentAsync has reached a terminal state (Paid/Declined) for
    /// the latest attempt - false while still Redirected/unresolved.
    /// </summary>
    public bool Resolved { get; set; }

    /// <summary>
    /// "Web" or "Mobile" - which surface initiated the latest attempt. Used by
    /// BackUrlReturn to pick the right redirect target once resolved.
    /// </summary>
    public string Platform { get; set; }
}
