using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Payments;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Payment
{
    /// <summary>
    /// mobile-v2's saved-card ("Payment Methods") surface - api/v2/payment-cards,
    /// versioned alongside api/v2/catalog/cart/checkout/order. Thin wrapper over
    /// ICustomerCardBindingService (Nop.Plugin.Payments.AmeriaVPos) - see that
    /// interface for why the real logic lives in the plugin instead of here.
    ///
    /// There is no $0 "just verify the card" call in AmeriaBank's vPOS API - adding a
    /// card makes a real, nominal charge (admin-configured, refunded immediately once
    /// the binding is confirmed). "start" returns a hosted pay-page URL the client opens
    /// (Linking.openURL); this app has no deep-linking return infra yet, so the client
    /// re-polls "status/{attemptId}" (or just refetches the card list) after returning
    /// from the browser rather than being told automatically.
    /// </summary>
    [Produces("application/json")]
    [Route("api/v2/payment-cards")]
    [Authorize]
    public class PaymentCardV2ApiController(
        ICustomerCardBindingService customerCardBindingService,
        IWorkContext workContext)
        : BaseApiController
    {
        public class BoundCardV2Model
        {
            public int Id { get; set; }
            public string CardPanMasked { get; set; }
            public string ExpDate { get; set; }
        }

        [HttpGet("")]
        public async Task<IActionResult> GetCards()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var cards = await customerCardBindingService.GetCardsAsync(customer.Id);

            return Ok(cards.Select(MapCard).ToList());
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartAddCard()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var result = await customerCardBindingService.StartAddCardAsync(customer);

            return Ok(new
            {
                success = result.Success,
                attemptId = result.AttemptId,
                paymentUrl = result.PaymentUrl,
                verificationAmountFormatted = result.VerificationAmountFormatted,
                message = result.Message
            });
        }

        // Ledger-only read (no live AmeriaBank call) - a lightweight poll for the client
        // to check after returning from the hosted pay page, before it just refetches
        // the card list outright.
        [HttpGet("status/{attemptId}")]
        public async Task<IActionResult> GetAddCardStatus(int attemptId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var result = await customerCardBindingService.GetAttemptStatusAsync(attemptId, customer.Id);

            return Ok(new { success = result.Success, resolved = result.Resolved, status = result.Status, message = result.Message });
        }

        [HttpDelete("{cardId}")]
        public async Task<IActionResult> RemoveCard(int cardId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var success = await customerCardBindingService.RemoveCardAsync(customer.Id, cardId);

            return Ok(new { success });
        }

        private static BoundCardV2Model MapCard(BoundCardInfo card) => new()
        {
            Id = card.Id,
            CardPanMasked = card.CardPanMasked,
            ExpDate = card.ExpDate
        };
    }
}
