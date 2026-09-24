using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Payments.AmeriaVPos.Domain;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Catalog;

namespace Nop.Plugin.Payments.AmeriaVPos.Services
{
    /// <summary>
    /// Card-binding logic on top of AmeriaBank vPOS's binding transactions - see
    /// ICustomerCardBindingService for why this is shared with the mobile API.
    /// </summary>
    public class CustomerCardBindingService : ICustomerCardBindingService
    {
        // Order-payment attempts already use 900_000_000+ (AmeriaVPosPaymentService) -
        // a distinct offset keeps binding-verification OrderIDs from ever colliding with
        // those even though they're separate auto-increment sequences (different tables).
        // A bound-card CHARGE (ChargeBoundCardAsync) is a real order payment though, so it
        // reuses the SAME 900_000_000 offset as the redirect flow - both write into
        // AmeriaVPosPaymentAttempt, the one ledger admin refund/cancel/reconciliation
        // already reads.
        private const int CardBindingOrderIdOffset = 950_000_000;
        private const int ChargeOrderIdOffset = 900_000_000;

        #region Fields

        private readonly IRepository<CardBindingAttempt> _bindingAttemptRepository;
        private readonly IRepository<CustomerBoundCard> _boundCardRepository;
        private readonly IRepository<AmeriaVPosPaymentAttempt> _paymentAttemptRepository;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IWebHelper _webHelper;
        private readonly AmeriaVPosSettings _ameriaVPosSettings;
        private readonly AmeriaVPosApiClient _apiClient;
        private readonly IPriceFormatter _priceFormatter;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public CustomerCardBindingService(
            IRepository<CardBindingAttempt> bindingAttemptRepository,
            IRepository<CustomerBoundCard> boundCardRepository,
            IRepository<AmeriaVPosPaymentAttempt> paymentAttemptRepository,
            IOrderProcessingService orderProcessingService,
            IWebHelper webHelper,
            AmeriaVPosSettings ameriaVPosSettings,
            AmeriaVPosApiClient apiClient,
            IPriceFormatter priceFormatter,
            ILogger logger)
        {
            _bindingAttemptRepository = bindingAttemptRepository;
            _boundCardRepository = boundCardRepository;
            _paymentAttemptRepository = paymentAttemptRepository;
            _orderProcessingService = orderProcessingService;
            _webHelper = webHelper;
            _ameriaVPosSettings = ameriaVPosSettings;
            _apiClient = apiClient;
            _priceFormatter = priceFormatter;
            _logger = logger;
        }

        #endregion

        #region Methods

        public async Task<CardBindingStartResult> StartAddCardAsync(Customer customer)
        {
            // Our own, unguessable identifier - never derived from CustomerId/OrderId
            // (see CustomerBoundCard's doc comment for why that matters).
            var cardHolderId = Guid.NewGuid().ToString("N");

            var attempt = new CardBindingAttempt
            {
                CustomerId = customer.Id,
                CardHolderId = cardHolderId,
                VerificationAmount = _ameriaVPosSettings.CardVerificationAmount,
                Status = CardBindingAttemptStatus.Started,
                CreatedOnUtc = DateTime.UtcNow
            };
            await _bindingAttemptRepository.InsertAsync(attempt);

            var backUrl = $"{_webHelper.GetStoreLocation()}ameriavpos/bindingbackurlreturn?attemptId={attempt.Id}";

            var initResponse = await _apiClient.InitPaymentAsync(new InitPaymentRequest
            {
                ClientID = _ameriaVPosSettings.ClientId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password,
                Amount = attempt.VerificationAmount,
                OrderID = CardBindingOrderIdOffset + attempt.Id,
                Currency = "051",
                Description = "MySnacks card verification",
                BackURL = backUrl,
                CardHolderID = cardHolderId
            });

            if (initResponse?.ResponseCode != 1)
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos card binding InitPayment failed for customer {customer.Id}, attempt {attempt.Id}: " +
                    $"{initResponse?.ResponseCode} {initResponse?.ResponseMessage}");
                attempt.Status = CardBindingAttemptStatus.Declined;
                attempt.ResolvedOnUtc = DateTime.UtcNow;
                await _bindingAttemptRepository.UpdateAsync(attempt);

                return new CardBindingStartResult
                {
                    Success = false,
                    Message = "Could not start card verification - please try again."
                };
            }

            attempt.PaymentId = initResponse.PaymentID;
            attempt.Status = CardBindingAttemptStatus.Redirected;
            await _bindingAttemptRepository.UpdateAsync(attempt);

            var paymentUrl = $"{_apiClient.PayBaseUrl}/Payments/Pay?id={initResponse.PaymentID}&lang=en";

            return new CardBindingStartResult
            {
                Success = true,
                AttemptId = attempt.Id,
                PaymentUrl = paymentUrl,
                VerificationAmountFormatted = await _priceFormatter.FormatPriceAsync(attempt.VerificationAmount)
            };
        }

        public async Task<CardBindingResolveResult> ResolveAddCardAsync(int attemptId)
        {
            var attempt = await _bindingAttemptRepository.GetByIdAsync(attemptId);
            if (attempt == null)
                return new CardBindingResolveResult { Success = false, Message = "Verification attempt not found." };

            if (attempt.ResolvedOnUtc.HasValue)
                return new CardBindingResolveResult
                {
                    Success = attempt.Status == CardBindingAttemptStatus.Bound,
                    Resolved = true,
                    Status = attempt.Status.ToString()
                };

            var details = await _apiClient.GetPaymentDetailsAsync(new PaymentDetailsRequest
            {
                PaymentID = attempt.PaymentId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password
            });

            if (details?.PaymentState != "payment_deposited")
            {
                attempt.Status = CardBindingAttemptStatus.Declined;
                attempt.ResolvedOnUtc = DateTime.UtcNow;
                await _bindingAttemptRepository.UpdateAsync(attempt);

                return new CardBindingResolveResult
                {
                    Success = false,
                    Resolved = true,
                    Status = attempt.Status.ToString(),
                    Message = "Card verification failed - please try again."
                };
            }

            //never trust CardHolderID/CardNumber/ExpDate from anywhere but this
            //authoritative pull
            var boundCard = new CustomerBoundCard
            {
                CustomerId = attempt.CustomerId,
                CardHolderId = details.CardHolderID ?? attempt.CardHolderId,
                CardPanMasked = details.CardNumber,
                ExpDate = details.ExpDate,
                IsActive = true,
                CreatedOnUtc = DateTime.UtcNow
            };
            await _boundCardRepository.InsertAsync(boundCard);

            attempt.Status = CardBindingAttemptStatus.Bound;
            attempt.ResolvedOnUtc = DateTime.UtcNow;
            await _bindingAttemptRepository.UpdateAsync(attempt);

            //refund the nominal verification charge - this was never a real purchase.
            //The binding itself is already confirmed good even if this call fails, so a
            //refund failure is logged (for manual follow-up) rather than failing the
            //whole add-card flow.
            var refundResponse = await _apiClient.RefundPaymentAsync(new RefundPaymentRequest
            {
                PaymentID = attempt.PaymentId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password,
                Amount = attempt.VerificationAmount
            });

            if (refundResponse?.ResponseCode != "00")
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos card verification refund failed for customer {attempt.CustomerId}, attempt {attempt.Id}: " +
                    $"{refundResponse?.ResponseCode} {refundResponse?.ResponseMessage}");
            }

            return new CardBindingResolveResult { Success = true, Resolved = true, Status = attempt.Status.ToString() };
        }

        public async Task<CardBindingResolveResult> GetAttemptStatusAsync(int attemptId, int customerId)
        {
            var attempt = await _bindingAttemptRepository.GetByIdAsync(attemptId);
            if (attempt == null || attempt.CustomerId != customerId)
                return new CardBindingResolveResult { Success = false, Message = "Verification attempt not found." };

            return new CardBindingResolveResult
            {
                Success = attempt.Status == CardBindingAttemptStatus.Bound,
                Resolved = attempt.ResolvedOnUtc.HasValue,
                Status = attempt.Status.ToString()
            };
        }

        public async Task<IList<BoundCardInfo>> GetCardsAsync(int customerId)
        {
            var cards = await _boundCardRepository.Table
                .Where(c => c.CustomerId == customerId && c.IsActive)
                .OrderByDescending(c => c.Id)
                .ToListAsync();

            return cards.Select(c => new BoundCardInfo
            {
                Id = c.Id,
                CardPanMasked = c.CardPanMasked,
                ExpDate = c.ExpDate,
                CreatedOnUtc = c.CreatedOnUtc
            }).ToList();
        }

        public async Task<bool> RemoveCardAsync(int customerId, int cardId)
        {
            var card = await _boundCardRepository.GetByIdAsync(cardId);
            if (card == null || card.CustomerId != customerId || !card.IsActive)
                return false;

            var response = await _apiClient.DeactivateBindingAsync(new DeactivateBindingRequest
            {
                ClientID = _ameriaVPosSettings.ClientId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password,
                CardHolderID = card.CardHolderId
            });

            if (response?.ResponseCode != "00")
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos DeactivateBinding failed for customer {customerId}, card {cardId}: " +
                    $"{response?.ResponseCode} {response?.ResponseMessage}");
                return false;
            }

            card.IsActive = false;
            card.DeactivatedOnUtc = DateTime.UtcNow;
            await _boundCardRepository.UpdateAsync(card);

            return true;
        }

        public async Task<ChargeBoundCardResult> ChargeBoundCardAsync(Order order, int customerId, int cardId)
        {
            var card = await _boundCardRepository.GetByIdAsync(cardId);
            if (card == null || card.CustomerId != customerId || !card.IsActive)
                return new ChargeBoundCardResult { Success = false, Message = "Card not found." };

            var attemptNumber = await _paymentAttemptRepository.Table.Where(a => a.OrderId == order.Id).CountAsync() + 1;

            var attempt = new AmeriaVPosPaymentAttempt
            {
                OrderId = order.Id,
                AttemptNumber = attemptNumber,
                RequestedAmount = order.OrderTotal,
                Status = AmeriaVPosPaymentAttemptStatus.Started,
                Platform = "Mobile",
                CreatedOnUtc = DateTime.UtcNow
            };
            await _paymentAttemptRepository.InsertAsync(attempt);

            var response = await _apiClient.MakeBindingPaymentAsync(new MakeBindingPaymentRequest
            {
                ClientID = _ameriaVPosSettings.ClientId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password,
                CardHolderID = card.CardHolderId,
                Amount = order.OrderTotal,
                OrderID = ChargeOrderIdOffset + attempt.Id,
                //MakeBindingPayment never actually redirects (it resolves synchronously),
                //but BackURL is a required field on the request regardless - reuse the
                //same return action the redirect flow uses, harmless since it's never hit.
                BackURL = $"{_webHelper.GetStoreLocation()}ameriavpos/backurlreturn?msOrderId={order.Id}",
                Description = $"MySnacks order #{order.Id}",
                Currency = "051"
            });

            if (response?.ResponseCode != "00" || response.PaymentState != "payment_deposited")
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos MakeBindingPayment failed for order {order.Id}, card {cardId}: " +
                    $"{response?.ResponseCode} {response?.PaymentState}");
                attempt.Status = AmeriaVPosPaymentAttemptStatus.Declined;
                attempt.ResolvedOnUtc = DateTime.UtcNow;
                await _paymentAttemptRepository.UpdateAsync(attempt);

                return new ChargeBoundCardResult
                {
                    Success = false,
                    Message = "Card payment declined - please try again or use a different card."
                };
            }

            //never trust the amount from anywhere but this response's own DepositedAmount
            attempt.PaymentId = response.PaymentID;
            attempt.ChargedAmount = response.DepositedAmount;
            attempt.Rrn = response.Rrn;
            attempt.Status = AmeriaVPosPaymentAttemptStatus.Paid;
            attempt.ResolvedOnUtc = DateTime.UtcNow;
            await _paymentAttemptRepository.UpdateAsync(attempt);

            //mark paid the proper way (not a raw field flip) so ProcessOrderPaidAsync
            //runs: vendor notification, reward points, etc. - same as the redirect flow.
            await _orderProcessingService.MarkOrderAsPaidAsync(order);

            return new ChargeBoundCardResult { Success = true };
        }

        #endregion
    }
}
