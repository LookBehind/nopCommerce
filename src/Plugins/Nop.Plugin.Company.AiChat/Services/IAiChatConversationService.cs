using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Plugin.Company.AiChat.Domain;

namespace Nop.Plugin.Company.AiChat.Services
{
    public partial interface IAiChatConversationService
    {
        /// <summary>
        /// Gets the customer's current (most recently started) conversation, creating one if
        /// none exists yet.
        /// </summary>
        Task<AiChatConversation> GetOrCreateCurrentConversationAsync(int customerId, int storeId);

        /// <summary>
        /// Starts a brand new conversation for the customer - old ones are kept, just no longer
        /// "current". Mirrors the mobile UI's "new conversation" reset icon.
        /// </summary>
        Task<AiChatConversation> StartNewConversationAsync(int customerId, int storeId);

        Task<IList<AiChatMessage>> GetMessagesAsync(int conversationId);

        Task<AiChatMessage> AddMessageAsync(int conversationId, bool isFromCustomer, string body, IList<int> suggestedProductIds = null);
    }
}
