using System.Text.Json;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Company.Insights.Models;
using Nop.Plugin.Company.Insights.Services;
using Nop.Services.Events;

namespace Nop.Plugin.Company.Insights.Infrastructure
{
    // Each consumer just enqueues a normalized trigger event onto the durable stream. Enqueue is
    // best-effort (never throws), so capturing an event can't break the request that produced it.
    // Time-derived events (delivery-approaching / day-closing) come from Hangfire jobs, not here.

    public class InsightsReviewAddedConsumer : IConsumer<EntityInsertedEvent<ProductReview>>
    {
        private readonly IInsightsEventService _events;
        public InsightsReviewAddedConsumer(IInsightsEventService events) => _events = events;

        public Task HandleEventAsync(EntityInsertedEvent<ProductReview> e)
        {
            var r = e.Entity;
            var payload = JsonSerializer.Serialize(new { productId = r.ProductId, rating = r.Rating, approved = r.IsApproved });
            return _events.EnqueueAsync(InsightsEventTypes.ReviewAdded, "ProductReview", r.Id, null, payload);
        }
    }

    public class InsightsOrderPlacedConsumer : IConsumer<OrderPlacedEvent>
    {
        private readonly IInsightsEventService _events;
        public InsightsOrderPlacedConsumer(IInsightsEventService events) => _events = events;

        public Task HandleEventAsync(OrderPlacedEvent e)
        {
            var o = e.Order;
            var payload = JsonSerializer.Serialize(new { total = o.OrderTotal, status = o.OrderStatusId, scheduleDate = o.ScheduleDate });
            return _events.EnqueueAsync(InsightsEventTypes.OrderPlaced, "Order", o.Id, o.CompanyId, payload);
        }
    }

    public class InsightsOrderCancelledConsumer : IConsumer<OrderCancelledEvent>
    {
        private readonly IInsightsEventService _events;
        public InsightsOrderCancelledConsumer(IInsightsEventService events) => _events = events;

        public Task HandleEventAsync(OrderCancelledEvent e)
        {
            var o = e.Order;
            var payload = JsonSerializer.Serialize(new { total = o.OrderTotal, scheduleDate = o.ScheduleDate });
            return _events.EnqueueAsync(InsightsEventTypes.OrderCancelled, "Order", o.Id, o.CompanyId, payload);
        }
    }

    public class InsightsProductCreatedConsumer : IConsumer<EntityInsertedEvent<Product>>
    {
        private readonly IInsightsEventService _events;
        public InsightsProductCreatedConsumer(IInsightsEventService events) => _events = events;

        public Task HandleEventAsync(EntityInsertedEvent<Product> e)
        {
            var p = e.Entity;
            var payload = JsonSerializer.Serialize(new { name = p.Name, vendorId = p.VendorId, published = p.Published });
            return _events.EnqueueAsync(InsightsEventTypes.ProductCreated, "Product", p.Id, null, payload);
        }
    }

    public class InsightsProductUpdatedConsumer : IConsumer<EntityUpdatedEvent<Product>>
    {
        private readonly IInsightsEventService _events;
        public InsightsProductUpdatedConsumer(IInsightsEventService events) => _events = events;

        public Task HandleEventAsync(EntityUpdatedEvent<Product> e)
        {
            var p = e.Entity;
            var payload = JsonSerializer.Serialize(new { name = p.Name, vendorId = p.VendorId, published = p.Published });
            return _events.EnqueueAsync(InsightsEventTypes.ProductUpdated, "Product", p.Id, null, payload);
        }
    }
}
