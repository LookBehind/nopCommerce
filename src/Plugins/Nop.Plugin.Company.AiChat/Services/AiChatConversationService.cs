using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Data;
using Nop.Plugin.Company.AiChat.Domain;

namespace Nop.Plugin.Company.AiChat.Services
{
    public partial class AiChatConversationService : IAiChatConversationService
    {
        private readonly IRepository<AiChatConversation> _conversationRepository;
        private readonly IRepository<AiChatMessage> _messageRepository;

        public AiChatConversationService(
            IRepository<AiChatConversation> conversationRepository,
            IRepository<AiChatMessage> messageRepository)
        {
            _conversationRepository = conversationRepository;
            _messageRepository = messageRepository;
        }

        public virtual async Task<AiChatConversation> GetOrCreateCurrentConversationAsync(int customerId, int storeId)
        {
            var existing = await _conversationRepository.GetAllAsync(query =>
                query.Where(c => c.CustomerId == customerId).OrderByDescending(c => c.CreatedOnUtc));

            var current = existing.FirstOrDefault();
            if (current != null)
                return current;

            return await StartNewConversationAsync(customerId, storeId);
        }

        public virtual async Task<AiChatConversation> StartNewConversationAsync(int customerId, int storeId)
        {
            var conversation = new AiChatConversation
            {
                CustomerId = customerId,
                StoreId = storeId,
                CreatedOnUtc = DateTime.UtcNow
            };

            await _conversationRepository.InsertAsync(conversation);
            return conversation;
        }

        public virtual async Task<IList<AiChatMessage>> GetMessagesAsync(int conversationId)
        {
            return await _messageRepository.GetAllAsync(query =>
                query.Where(m => m.ConversationId == conversationId).OrderBy(m => m.CreatedOnUtc));
        }

        public virtual async Task<AiChatMessage> AddMessageAsync(int conversationId, bool isFromCustomer, string body, IList<int> suggestedProductIds = null)
        {
            var message = new AiChatMessage
            {
                ConversationId = conversationId,
                IsFromCustomer = isFromCustomer,
                Body = body,
                SuggestedProductIdsCsv = suggestedProductIds is { Count: > 0 } ? string.Join(",", suggestedProductIds) : null,
                CreatedOnUtc = DateTime.UtcNow
            };

            await _messageRepository.InsertAsync(message);
            return message;
        }
    }
}
