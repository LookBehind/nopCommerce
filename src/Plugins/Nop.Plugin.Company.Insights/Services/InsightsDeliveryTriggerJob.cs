using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LinqToDB;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>Bodies of the per-slot / per-store time-derived trigger jobs (Hangfire-invoked).</summary>
    public interface IInsightsDeliveryTriggerJob
    {
        /// <summary>Fires 40 min before a delivery slot: emits one delivery-approaching event per due order.</summary>
        Task RunApproachingAsync(int storeId, string slotHHmm);

        /// <summary>Fires after the last slot: emits one day-closing event per company that had a
        /// delivery scheduled today OR still has open (pending/processing) orders to chase.</summary>
        Task RunDayClosingAsync(int storeId);
    }

    public class InsightsDeliveryTriggerJob : IInsightsDeliveryTriggerJob
    {
        private const int LocalUtcOffsetHours = 4;   // ScheduleDate is UTC+4 wall-clock
        private const int DedupeWindowMinutes = 12 * 60;

        private readonly INopDataProvider _dataProvider;
        private readonly IInsightsEventService _events;
        private readonly IInsightsAgentConfigService _configs;
        private readonly ILogger _logger;

        public InsightsDeliveryTriggerJob(INopDataProvider dataProvider, IInsightsEventService events, IInsightsAgentConfigService configs, ILogger logger)
        {
            _dataProvider = dataProvider;
            _events = events;
            _configs = configs;
            _logger = logger;
        }

        public async Task RunApproachingAsync(int storeId, string slotHHmm)
        {
            if (!_events.Enabled || !TimeSpan.TryParse(slotHHmm, out var slot))
                return;

            try
            {
                var today = DateTime.UtcNow.AddHours(LocalUtcOffsetHours).Date; // delivery-frame (UTC+4) date
                var slotStart = today + slot;
                var slotEnd = slotStart.AddMinutes(1);

                var orders = await _dataProvider.GetTable<Order>()
                    .Where(o => !o.Deleted && o.StoreId == storeId
                        && (o.OrderStatusId == (int)OrderStatus.Pending || o.OrderStatusId == (int)OrderStatus.Processing)
                        && o.ScheduleDate >= slotStart && o.ScheduleDate < slotEnd)
                    .Select(o => new { o.Id, o.CompanyId, o.OrderTotal, o.ScheduleDate })
                    .ToListAsync();

                foreach (var o in orders)
                {
                    var payload = JsonSerializer.Serialize(new { slot = slotHHmm, scheduleDate = o.ScheduleDate, total = o.OrderTotal, storeId });
                    await _events.EnqueueUniqueAsync(InsightsEventTypes.DeliveryApproaching, "Order", o.Id, o.CompanyId, payload, DedupeWindowMinutes);
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync($"Insights delivery-approaching job failed (store {storeId}, slot {slotHHmm})", ex);
            }
        }

        public async Task RunDayClosingAsync(int storeId)
        {
            if (!_events.Enabled)
                return;

            try
            {
                var dateStr = DateTime.UtcNow.AddHours(LocalUtcOffsetHours).ToString("yyyy-MM-dd"); // delivery-frame (UTC+4) date

                // Day-closing is a daily wake-up, NOT an order query. Whether there's anything worth
                // sending is the agent's call (it reads live data via tools and may return NO_REPORT).
                // The trigger's only job is to wake the right agents once a day; the sole reason it needs
                // the data layer is to know WHICH companies to emit a per-company event for — which is
                // simply the set of companies that actually have a day-closing automation configured, not
                // whatever orders happen to exist today. (Formerly it gated on "orders scheduled today",
                // so a quiet delivery day silently skipped a company's whole open-order backlog.)
                var dayClosing = (await _configs.ListAsync())
                    .Where(a => a.Enabled && a.EventType == InsightsEventTypes.DayClosing)
                    .ToList();
                if (dayClosing.Count == 0)
                    return;

                // One event per company that has a company-scoped day-closing automation.
                foreach (var companyId in dayClosing.Where(a => a.CompanyId.HasValue).Select(a => a.CompanyId.Value).Distinct())
                {
                    var payload = JsonSerializer.Serialize(new { date = dateStr, companyId, storeId });
                    await _events.EnqueueUniqueAsync(InsightsEventTypes.DayClosing, "Company", companyId, companyId, payload, DedupeWindowMinutes);
                }

                // One company-less event so any global (unscoped) day-closing automation runs once.
                if (dayClosing.Any(a => !a.CompanyId.HasValue))
                {
                    var payload = JsonSerializer.Serialize(new { date = dateStr, scope = "global", storeId });
                    await _events.EnqueueUniqueAsync(InsightsEventTypes.DayClosing, "DayClosing", 0, null, payload, DedupeWindowMinutes);
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync($"Insights day-closing job failed (store {storeId})", ex);
            }
        }
    }
}
