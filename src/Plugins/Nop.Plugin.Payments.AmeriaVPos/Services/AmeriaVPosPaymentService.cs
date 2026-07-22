using System;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Data;
using Nop.Plugin.Payments.AmeriaVPos.Domain;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Payments.AmeriaVPos.Services
{
    /// <summary>
    /// Core AmeriaBank vPOS payment logic - see IAmeriaVPosPaymentService for why this is
    /// shared between the web IPaymentMethod flow and the mobile order-confirmation API.
    /// </summary>
    public class AmeriaVPosPaymentService : IAmeriaVPosPaymentService
    {
        #region Fields

        private readonly ICustomerService _customerService;
        private readonly ICompanyAllowancePaymentMethod _companyAllowancePaymentMethod;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IRepository<AmeriaVPosPaymentAttempt> _attemptRepository;
        private readonly IWebHelper _webHelper;
        private readonly AmeriaVPosSettings _ameriaVPosSettings;
        private readonly AmeriaVPosApiClient _apiClient;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public AmeriaVPosPaymentService(
            ICustomerService customerService,
            ICompanyAllowancePaymentMethod companyAllowancePaymentMethod,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IRepository<AmeriaVPosPaymentAttempt> attemptRepository,
            IWebHelper webHelper,
            AmeriaVPosSettings ameriaVPosSettings,
            AmeriaVPosApiClient apiClient,
            ILogger logger)
        {
            _customerService = customerService;
            _companyAllowancePaymentMethod = companyAllowancePaymentMethod;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _attemptRepository = attemptRepository;
            _webHelper = webHelper;
            _ameriaVPosSettings = ameriaVPosSettings;
            _apiClient = apiClient;
            _logger = logger;
        }

        #endregion

        #region Utilities

        private async Task<AmeriaVPosPaymentAttempt> GetLatestAttemptAsync(int orderId)
        {
            return await _attemptRepository.Table
                .Where(a => a.OrderId == orderId)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Maps an AmeriaBank Table-2 PaymentState to our attempt status. Unknown/unset
        /// states (payment_started, payment_approved, payment_autoauthorized - none of
        /// which apply to a single-stage, non-binding integration) stay Redirected.
        /// </summary>
        private static AmeriaVPosPaymentAttemptStatus MapPaymentState(string paymentState) => paymentState switch
        {
            "payment_deposited" => AmeriaVPosPaymentAttemptStatus.Paid,
            "payment_declined" => AmeriaVPosPaymentAttemptStatus.Declined,
            "payment_refunded" => AmeriaVPosPaymentAttemptStatus.Refunded,
            "payment_void" => AmeriaVPosPaymentAttemptStatus.Cancelled,
            _ => AmeriaVPosPaymentAttemptStatus.Redirected
        };

        #endregion

        #region Methods

        public async Task<AmeriaVPosPaymentResult> InitiateOrCompletePaymentAsync(Order order, string platform = "Web")
        {
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

            var balance = await _companyAllowancePaymentMethod.GetCustomerRemainingAllowance(
                new CustomerBalanceRequest { Customer = customer, OrderDateUtc = order.ScheduleDate });

            //null means "nothing to draw against" here (zero limit, Allowance Excempt role, or
            //no company at all) - not an error, just the same as RemainingAllowance == 0
            var remainingAllowance = balance?.RemainingAllowance ?? 0M;

            //An order is never split between allowance and card - per-company invoice
            //reconciliation attributes a whole order to one payer or the other (a
            //completed AmeriaVPos charge >0 excludes the ENTIRE order from that company's
            //allowance invoice), so a partial allowance/partial card charge would silently
            //under-bill the company for the portion it actually covered. If the allowance
            //can't cover the full order, none of it is drawn - the whole total goes to card.
            if (remainingAllowance >= order.OrderTotal)
            {
                //fully covered - mark paid the proper way (not a raw field flip) so
                //ProcessOrderPaidAsync runs: vendor notification, reward points, etc.
                await _orderProcessingService.MarkOrderAsPaidAsync(order);

                return new AmeriaVPosPaymentResult
                {
                    RequiresPayment = false,
                    AmountDue = 0M,
                    AmountCoveredByAllowance = order.OrderTotal
                };
            }

            var amountCoveredByAllowance = 0M;
            var amountDue = order.OrderTotal;

            var attemptNumber = await _attemptRepository.Table.Where(a => a.OrderId == order.Id).CountAsync() + 1;

            var attempt = new AmeriaVPosPaymentAttempt
            {
                OrderId = order.Id,
                AttemptNumber = attemptNumber,
                RequestedAmount = amountDue,
                Status = AmeriaVPosPaymentAttemptStatus.Started,
                Platform = platform,
                CreatedOnUtc = DateTime.UtcNow
            };
            await _attemptRepository.InsertAsync(attempt);

            //"msOrderId", not "orderId" - collides with AmeriaBank's own OrderID field on
            //their return redirect, see the comment on BackUrlReturn
            var backUrl = $"{_webHelper.GetStoreLocation()}ameriavpos/backurlreturn?msOrderId={order.Id}";

            var initResponse = await _apiClient.InitPaymentAsync(new InitPaymentRequest
            {
                ClientID = _ameriaVPosSettings.ClientId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password,
                Amount = amountDue,
                OrderID = attempt.Id,
                Currency = "051",
                Description = $"MySnacks order #{order.Id}",
                BackURL = backUrl
            });

            if (initResponse?.ResponseCode != 1)
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos InitPayment failed for order {order.Id}, attempt {attempt.Id}: " +
                    $"{initResponse?.ResponseCode} {initResponse?.ResponseMessage}");
                attempt.Status = AmeriaVPosPaymentAttemptStatus.Declined;
                attempt.ResolvedOnUtc = DateTime.UtcNow;
                await _attemptRepository.UpdateAsync(attempt);

                return new AmeriaVPosPaymentResult
                {
                    RequiresPayment = true,
                    AmountDue = amountDue,
                    AmountCoveredByAllowance = amountCoveredByAllowance,
                    Status = attempt.Status.ToString(),
                    Platform = attempt.Platform
                };
            }

            attempt.PaymentId = initResponse.PaymentID;
            attempt.Status = AmeriaVPosPaymentAttemptStatus.Redirected;
            await _attemptRepository.UpdateAsync(attempt);

            var paymentUrl = $"{_apiClient.PayBaseUrl}/Payments/Pay?id={initResponse.PaymentID}&lang=en";

            return new AmeriaVPosPaymentResult
            {
                RequiresPayment = true,
                PaymentUrl = paymentUrl,
                AmountDue = amountDue,
                AmountCoveredByAllowance = amountCoveredByAllowance,
                Status = attempt.Status.ToString(),
                Platform = attempt.Platform
            };
        }

        public async Task<AmeriaVPosPaymentResult> GetLatestAttemptStatusAsync(Order order)
        {
            var attempt = await GetLatestAttemptAsync(order.Id);
            if (attempt == null)
                return new AmeriaVPosPaymentResult { Status = AmeriaVPosPaymentAttemptStatus.Started.ToString() };

            return new AmeriaVPosPaymentResult
            {
                Status = attempt.Status.ToString(),
                AmountDue = attempt.RequestedAmount,
                Resolved = attempt.ResolvedOnUtc.HasValue,
                Platform = attempt.Platform
            };
        }

        public async Task<AmeriaVPosPaymentResult> ResolvePaymentAsync(Order order)
        {
            var attempt = await GetLatestAttemptAsync(order.Id);
            if (attempt == null || string.IsNullOrEmpty(attempt.PaymentId))
                return new AmeriaVPosPaymentResult { Status = AmeriaVPosPaymentAttemptStatus.Started.ToString() };

            if (attempt.ResolvedOnUtc.HasValue)
                return new AmeriaVPosPaymentResult
                {
                    Status = attempt.Status.ToString(),
                    AmountDue = attempt.RequestedAmount,
                    Resolved = true,
                    Platform = attempt.Platform
                };

            var details = await _apiClient.GetPaymentDetailsAsync(new PaymentDetailsRequest
            {
                PaymentID = attempt.PaymentId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password
            });

            var newStatus = MapPaymentState(details?.PaymentState);

            if (newStatus == AmeriaVPosPaymentAttemptStatus.Paid)
            {
                //never trust the amount from the redirect - only from this authoritative pull
                attempt.ChargedAmount = details.DepositedAmount;
                attempt.Rrn = details.Rrn;
                attempt.Status = AmeriaVPosPaymentAttemptStatus.Paid;
                attempt.ResolvedOnUtc = DateTime.UtcNow;
                await _attemptRepository.UpdateAsync(attempt);

                //order.OrderTotal was never reduced - the allowance already covered the rest,
                //so a successful card charge for the shortfall means the whole order is paid
                await _orderProcessingService.MarkOrderAsPaidAsync(order);
            }
            else if (newStatus == AmeriaVPosPaymentAttemptStatus.Declined)
            {
                attempt.Status = AmeriaVPosPaymentAttemptStatus.Declined;
                attempt.ResolvedOnUtc = DateTime.UtcNow;
                await _attemptRepository.UpdateAsync(attempt);

                //restore the cart, mirroring Idram's Fail() action - the order stays Pending
                //forever otherwise, with an already-emptied cart and no easy way to retry
                await _orderProcessingService.ReOrderAsync(order);
                await _orderProcessingService.DeleteOrderAsync(order);
            }

            return new AmeriaVPosPaymentResult
            {
                Status = attempt.Status.ToString(),
                AmountDue = attempt.RequestedAmount,
                Resolved = attempt.ResolvedOnUtc.HasValue,
                Platform = attempt.Platform
            };
        }

        public async Task<bool> RefundAsync(Order order, decimal amount)
        {
            var attempt = await GetLatestAttemptAsync(order.Id);
            if (attempt?.Status != AmeriaVPosPaymentAttemptStatus.Paid)
                return false;

            var response = await _apiClient.RefundPaymentAsync(new RefundPaymentRequest
            {
                PaymentID = attempt.PaymentId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password,
                Amount = amount
            });

            if (response?.ResponseCode != "00")
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos RefundPayment failed for order {order.Id}: {response?.ResponseCode} {response?.ResponseMessage}");
                return false;
            }

            attempt.Status = AmeriaVPosPaymentAttemptStatus.Refunded;
            await _attemptRepository.UpdateAsync(attempt);

            if (amount >= order.OrderTotal)
                await _orderProcessingService.RefundOfflineAsync(order);
            else
                await _orderProcessingService.PartiallyRefundOfflineAsync(order, amount);

            return true;
        }

        public async Task<bool> CancelAsync(Order order)
        {
            var attempt = await GetLatestAttemptAsync(order.Id);
            if (attempt?.Status != AmeriaVPosPaymentAttemptStatus.Paid)
                return false;

            var response = await _apiClient.CancelPaymentAsync(new CancelPaymentRequest
            {
                PaymentID = attempt.PaymentId,
                Username = _ameriaVPosSettings.Username,
                Password = _ameriaVPosSettings.Password
            });

            if (response?.ResponseCode != "00")
            {
                await _logger.ErrorAsync(
                    $"AmeriaVPos CancelPayment failed for order {order.Id}: {response?.ResponseCode} {response?.ResponseMessage}");
                return false;
            }

            attempt.Status = AmeriaVPosPaymentAttemptStatus.Cancelled;
            await _attemptRepository.UpdateAsync(attempt);

            await _orderProcessingService.VoidOfflineAsync(order);

            return true;
        }

        #endregion
    }
}
