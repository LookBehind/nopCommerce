using System;
using System.Linq;
using System.Threading.Tasks;
using Nop.Data;
using Nop.Plugin.Payments.AmeriaVPos.Domain;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using IScheduleTask = Nop.Services.Tasks.IScheduleTask;

namespace Nop.Plugin.Payments.AmeriaVPos.ScheduledTasks
{
    /// <summary>
    /// Resolves AmeriaVPos payment attempts the customer never returned to BackURL for
    /// (browser closed, app killed, network dropped). Pulls the authoritative status one
    /// more time in case payment actually succeeded and we just missed the redirect, then
    /// gives up and restores the cart if AmeriaBank also has no resolution after the
    /// configured timeout - mirroring the abandoned-order handling Idram's Fail() action
    /// already does for the web redirect-failure case.
    /// </summary>
    public class AmeriaVPosReconciliationTask : IScheduleTask
    {
        public const string TASK_TYPE = "Nop.Plugin.Payments.AmeriaVPos.ScheduledTasks.AmeriaVPosReconciliationTask";
        public const string TASK_NAME = "AmeriaVPos abandoned payment reconciliation";

        #region Fields

        private readonly IRepository<AmeriaVPosPaymentAttempt> _attemptRepository;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IAmeriaVPosPaymentService _ameriaVPosPaymentService;
        private readonly AmeriaVPosSettings _ameriaVPosSettings;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public AmeriaVPosReconciliationTask(
            IRepository<AmeriaVPosPaymentAttempt> attemptRepository,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IAmeriaVPosPaymentService ameriaVPosPaymentService,
            AmeriaVPosSettings ameriaVPosSettings,
            ILogger logger)
        {
            _attemptRepository = attemptRepository;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _ameriaVPosPaymentService = ameriaVPosPaymentService;
            _ameriaVPosSettings = ameriaVPosSettings;
            _logger = logger;
        }

        #endregion

        #region Methods

        public async Task ExecuteAsync()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-_ameriaVPosSettings.AbandonedAttemptTimeoutMinutes);

            var staleAttempts = await _attemptRepository.Table
                .Where(a => a.StatusId == (int)AmeriaVPosPaymentAttemptStatus.Redirected && a.CreatedOnUtc < cutoff)
                .ToListAsync();

            foreach (var attempt in staleAttempts)
            {
                var order = await _orderService.GetOrderByIdAsync(attempt.OrderId);
                if (order == null)
                    continue;

                //one last authoritative pull - covers the case where the payment actually
                //succeeded but the customer's browser never made it back to BackURL
                var result = await _ameriaVPosPaymentService.ResolvePaymentAsync(order);
                if (result.Resolved)
                    continue;

                //still unresolved after the timeout - give up, restore the cart, matching
                //Idram's Fail() action for the equivalent web redirect-failure case
                var freshAttempt = await _attemptRepository.GetByIdAsync(attempt.Id);
                if (freshAttempt == null || freshAttempt.Status != AmeriaVPosPaymentAttemptStatus.Redirected)
                    continue;

                freshAttempt.Status = AmeriaVPosPaymentAttemptStatus.Abandoned;
                freshAttempt.ResolvedOnUtc = DateTime.UtcNow;
                await _attemptRepository.UpdateAsync(freshAttempt);

                await _logger.InformationAsync(
                    $"AmeriaVPos: order {order.Id} attempt {attempt.Id} abandoned after " +
                    $"{_ameriaVPosSettings.AbandonedAttemptTimeoutMinutes} minutes with no resolution - restoring cart.");

                await _orderProcessingService.ReOrderAsync(order);
                await _orderProcessingService.DeleteOrderAsync(order);
            }
        }

        #endregion
    }
}
