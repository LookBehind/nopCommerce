using System;

namespace Nop.Plugin.Company.Insights.Models
{
    /// <summary>
    /// The supported background-agent trigger event types (the registry). Each has a producer:
    /// nopCommerce event consumers for the DB events, and time-based Hangfire jobs for the rest.
    /// See docs/BACKGROUND-AGENTS.md §1/§6.
    /// </summary>
    public static class InsightsEventTypes
    {
        public const string ReviewAdded = "review-added";
        public const string ReviewTriaged = "review-triaged";
        public const string OrderPlaced = "order-placed";
        public const string OrderCancelled = "order-cancelled";
        public const string DeliveryApproaching = "delivery-approaching";
        public const string DayClosing = "day-closing";
        public const string ProductCreated = "product-created";
        public const string ProductUpdated = "product-updated";
    }

    /// <summary>One row of the durable agent-event outbox (the trigger stream).</summary>
    public class InsightsAgentEvent
    {
        public long Id { get; set; }
        public string EventType { get; set; }
        public string EntityType { get; set; }
        public int? EntityId { get; set; }
        public int? CompanyId { get; set; }
        public string Payload { get; set; }
        public string Status { get; set; }        // new | processed
        public DateTime OccurredAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
