using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Orders;

namespace Nop.Services.Payments;

/// <summary>
/// Card-binding (saved/tokenized card) logic on top of AmeriaBank vPOS's binding
/// transactions - shared between the plugin's own controller (hosted-page return) and
/// the mobile api/v2/payment-cards API, same reasoning as IAmeriaVPosPaymentService.
///
/// There is no $0 "just verify the card" call in AmeriaBank's API - creating a binding
/// requires a real, nominal charge (AmeriaVPosSettings.CardVerificationAmount), which is
/// refunded immediately once the binding is confirmed.
/// </summary>
public interface ICustomerCardBindingService
{
    /// <summary>
    /// Starts adding a new card: generates a fresh CardHolderID, calls InitPayment for
    /// the configured verification amount, and returns the hosted pay-page URL to open.
    /// </summary>
    Task<CardBindingStartResult> StartAddCardAsync(Nop.Core.Domain.Customers.Customer customer);

    /// <summary>
    /// Pulls the authoritative status for a binding attempt from AmeriaBank. On a
    /// successful charge, persists the new CustomerBoundCard and refunds the
    /// verification charge. Never trust a redirect querystring instead of this.
    /// </summary>
    Task<CardBindingResolveResult> ResolveAddCardAsync(int attemptId);

    /// <summary>
    /// Reads a binding attempt's status from the local ledger only - no live AmeriaBank
    /// call. Used for a lightweight mobile poll after returning from the hosted pay page.
    /// </summary>
    Task<CardBindingResolveResult> GetAttemptStatusAsync(int attemptId, int customerId);

    /// <summary>
    /// Lists the customer's active saved cards.
    /// </summary>
    Task<IList<BoundCardInfo>> GetCardsAsync(int customerId);

    /// <summary>
    /// Removes a saved card (ownership-checked) via DeactivateBinding and marks it
    /// inactive locally.
    /// </summary>
    Task<bool> RemoveCardAsync(int customerId, int cardId);

    /// <summary>
    /// Charges a customer's saved card (ownership-checked) the order's full total via
    /// MakeBindingPayment - a direct, synchronous charge, no redirect. On success marks
    /// the order Paid the same way IAmeriaVPosPaymentService.ResolvePaymentAsync does.
    /// </summary>
    Task<ChargeBoundCardResult> ChargeBoundCardAsync(Order order, int customerId, int cardId);
}

public class CardBindingStartResult
{
    public bool Success { get; set; }
    public int AttemptId { get; set; }
    public string PaymentUrl { get; set; }
    public string Message { get; set; }
}

public class CardBindingResolveResult
{
    public bool Success { get; set; }
    public bool Resolved { get; set; }
    public string Status { get; set; }
    public string Message { get; set; }
}

public class BoundCardInfo
{
    public int Id { get; set; }
    public string CardPanMasked { get; set; }
    public string ExpDate { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}

public class ChargeBoundCardResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
}
