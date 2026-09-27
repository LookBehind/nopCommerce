using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.AiChat.Domain;

namespace Nop.Plugin.Company.AiChat.Services
{
    public class AiChatTurnResult
    {
        public AiChatMessage AssistantMessage { get; set; }
        public IList<int> SuggestedProductIds { get; set; } = new List<int>();
    }

    public partial interface IAiChatService
    {
        /// <summary>
        /// Appends the customer's message to their current conversation, asks the model for a
        /// reply (letting it call search_products against the real catalog as needed), persists
        /// and returns the assistant's turn.
        /// </summary>
        Task<AiChatTurnResult> SendMessageAsync(Customer customer, int storeId, string userText, CancellationToken cancellationToken = default);
    }
}
