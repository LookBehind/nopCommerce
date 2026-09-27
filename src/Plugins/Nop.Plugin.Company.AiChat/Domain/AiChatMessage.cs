using System;
using Nop.Core;

namespace Nop.Plugin.Company.AiChat.Domain
{
    /// <summary>
    /// One turn in an AiChatConversation - either the customer's own text, or the assistant's
    /// reply. SuggestedProductIdsCsv carries whatever real product ids the assistant's
    /// search_products tool call turned up while answering this turn (see AiChatService),
    /// so the mobile client can render them as the same product cards the mock-up used,
    /// without ever needing the model to reliably echo ids back in its own prose.
    /// </summary>
    public partial class AiChatMessage : BaseEntity
    {
        /// <summary>
        /// Gets or sets the owning conversation's identifier
        /// </summary>
        public int ConversationId { get; set; }

        /// <summary>
        /// Gets or sets whether this message was authored by the customer (true) or the
        /// assistant (false) - mirrors SupportCaseMessage.IsStaff's role-flag pattern.
        /// </summary>
        public bool IsFromCustomer { get; set; }

        /// <summary>
        /// Gets or sets the message text
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Gets or sets a comma-separated list of real Product ids to render as product cards
        /// under this message, if any. Null/empty for plain text turns.
        /// </summary>
        public string SuggestedProductIdsCsv { get; set; }

        /// <summary>
        /// Gets or sets the date and time (UTC) this message was created
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }
    }
}
