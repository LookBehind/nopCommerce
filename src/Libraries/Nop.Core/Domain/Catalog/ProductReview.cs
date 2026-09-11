using System;

namespace Nop.Core.Domain.Catalog
{
    /// <summary>
    /// Represents a product review
    /// </summary>
    public partial class ProductReview : BaseEntity
    {
        /// <summary>
        /// Gets or sets the customer identifier
        /// </summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// Gets or sets the product identifier
        /// </summary>
        public int ProductId { get; set; }

        /// <summary>
        /// Gets or sets the order item this review is scoped to. Null for reviews created before
        /// order-item-scoped reviews existed - those stay product-only and are not attributable to
        /// a specific order. New reviews always set this: one review per order item, not per
        /// (customer, product), so the same product ordered in two different orders can be
        /// reviewed independently in each.
        /// </summary>
        public int? OrderItemId { get; set; }

        /// <summary>
        /// Gets or sets the store identifier
        /// </summary>
        public int StoreId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the content is approved
        /// </summary>
        public bool IsApproved { get; set; }

        /// <summary>
        /// Gets or sets the title
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the review text
        /// </summary>
        public string ReviewText { get; set; }

        /// <summary>
        /// Gets or sets the reply text
        /// </summary>
        public string ReplyText { get; set; }

        /// <summary>
        /// Gets or sets the value indicating whether the customer is already notified of the reply to review
        /// </summary>
        public bool CustomerNotifiedOfReply { get; set; }

        /// <summary>
        /// Review rating
        /// </summary>
        public int Rating { get; set; }

        /// <summary>
        /// Review helpful votes total
        /// </summary>
        public int HelpfulYesTotal { get; set; }

        /// <summary>
        /// Review not helpful votes total
        /// </summary>
        public int HelpfulNoTotal { get; set; }

        /// <summary>
        /// Gets or sets the date and time of instance creation
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets the customer (MySnacks employee) who triaged/approved this review. MySnacks addition.
        /// </summary>
        public int? TriagedByCustomerId { get; set; }

        /// <summary>
        /// Gets or sets when the review was triaged/approved (UTC). Triage time is derived as
        /// TriagedOnUtc - CreatedOnUtc. MySnacks addition.
        /// </summary>
        public DateTime? TriagedOnUtc { get; set; }

        /// <summary>
        /// Gets or sets free-text resolution / triage notes recorded by the employee. MySnacks addition.
        /// </summary>
        public string ResolutionDetails { get; set; }
    }
}
