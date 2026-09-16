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

        /// <summary>Fires after the last slot: emits one day-closing event per company that had deliveries today.</summary>
        Task RunDayClosingAsync(int storeId);
    }

    public class InsightsDeliveryTriggerJob : IInsightsDeliveryTriggerJob
    {
        private const int LocalUtcOffsetHours = 4;   // ScheduleDate is UTC+4 wall-clock
        private const int DedupeWindowHours = 12;

        private readonly INopDataProvider _dataProvider;
        private readonly IInsightsEventService _events;
        private readonly ILogger _logger;

        public InsightsDeliveryTriggerJob(INopDataProvider dataProvider, IInsightsEventService events, ILogger logger)
        {
            _dataProvider = dataProvider;
            _events = events;
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
                    await _events.EnqueueUniqueAsync(InsightsEventTypes.DeliveryApproaching, "Order", o.Id, o.CompanyId, payload, DedupeWindowHours);
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
                var today = DateTime.UtcNow.AddHours(LocalUtcOffsetHours).Date;
                var dayEnd = today.AddDays(1);

                var byCompany = await _dataProvider.GetTable<Order>()
                    .Where(o => !o.Deleted && o.StoreId == storeId && o.CompanyId != null
                        && o.ScheduleDate >= today && o.ScheduleDate < dayEnd)
                    .GroupBy(o => o.CompanyId)
                    .Select(g => new { CompanyId = g.Key, Orders = g.Count() })
                    .ToListAsync();

                foreach (var c in byCompany)
                {
                    var payload = JsonSerializer.Serialize(new { date = today.ToString("yyyy-MM-dd"), orders = c.Orders, storeId });
                    await _events.EnqueueUniqueAsync(InsightsEventTypes.DayClosing, "Company", c.CompanyId, c.CompanyId, payload, DedupeWindowHours);
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync($"Insights day-closing job failed (store {storeId})", ex);
            }
        }
    }
}
