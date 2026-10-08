using System.Threading.Tasks;
using Nop.Core.Domain.Messages;
using Nop.Services.Events;

namespace Nop.Plugin.Company.Support.EventConsumer
{
    /// <summary>
    /// Registers SupportCase.* tokens in the admin message-template token picker. SupportCase
    /// has no core AddXTokensAsync method to hang a token group off of (it's a plugin entity),
    /// so this uses the same AdditionalTokensAddedEvent hook Notifications.Manager already uses
    /// for Order.* plugin tokens (see MessageTemplateOrderTokenProvider).
    /// </summary>
    public class SupportCaseAdditionalTokensProvider : IConsumer<AdditionalTokensAddedEvent>
    {
        private readonly string[] _allowedTokens =
        {
            "%SupportCase.Id%",
            "%SupportCase.Category%",
            "%SupportCase.Status%",
            "%SupportCase.Description%",
            "%SupportCase.SelfUrl%",
            "%SupportCase.NewMessageText%"
        };

        public Task HandleEventAsync(AdditionalTokensAddedEvent eventMessage)
        {
            eventMessage.AddTokens(_allowedTokens);
            return Task.CompletedTask;
        }
    }
}
