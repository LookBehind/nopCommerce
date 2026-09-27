using System;
using Nop.Core;

namespace Nop.Plugin.Company.AiChat.Domain
{
    /// <summary>
    /// One AI Assistant conversation thread. A customer has at most one "current" conversation
    /// (the most recently created row for that CustomerId) - tapping "new conversation" in the
    /// mobile UI starts another row rather than deleting history, mirroring how SupportCase
    /// keeps every case rather than overwriting it.
    /// </summary>
    public partial class AiChatConversation : BaseEntity
    {
        /// <summary>
        /// Gets or sets the customer identifier who owns this conversation
        /// </summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// Gets or sets the store identifier (multi-tenant scoping, same as every other
        /// customer-facing entity in this codebase)
        /// </summary>
        public int StoreId { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) the conversation was started
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }
    }
}
