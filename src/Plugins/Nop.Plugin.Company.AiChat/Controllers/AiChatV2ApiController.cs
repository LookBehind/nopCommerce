using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.AiChat.Security;
using Nop.Plugin.Company.AiChat.Services;
using Nop.Services.Customers;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Company.AiChat.Controllers
{
    /// <summary>
    /// mobile-v2's AI Assistant chat surface. Gated behind the AiChatBetaTester customer role
    /// (see Security.AiChatRoles) so the feature can be turned on for specific accounts without
    /// a client-side build flag - every action 403s for a customer who doesn't hold the role.
    /// </summary>
    [Produces("application/json")]
    [Route("api/v2/aichat")]
    [Authorize]
    public class AiChatV2ApiController(
        IAiChatService aiChatService,
        IAiChatConversationService conversationService,
        ICustomerService customerService,
        IWorkContext workContext,
        IStoreContext storeContext)
        : BaseApiController
    {
        public class AiChatMessageV2Model
        {
            public int Id { get; set; }
            public string Role { get; set; }
            public string Text { get; set; }
            public List<int> ProductIds { get; set; }
            public DateTime CreatedOnUtc { get; set; }
        }

        public class SendAiChatMessageV2Model
        {
            public string Text { get; set; }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var enabled = await IsEntitledAsync(customer);
            return Ok(new { enabled });
        }

        [HttpGet("messages")]
        public async Task<IActionResult> GetMessages()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            if (!await IsEntitledAsync(customer))
                return Forbid();

            var store = await storeContext.GetCurrentStoreAsync();
            var conversation = await conversationService.GetOrCreateCurrentConversationAsync(customer.Id, store.Id);
            var messages = await conversationService.GetMessagesAsync(conversation.Id);

            return Ok(messages.Select(Map).ToList());
        }

        [HttpPost("messages")]
        public async Task<IActionResult> SendMessage([FromBody] SendAiChatMessageV2Model model)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            if (!await IsEntitledAsync(customer))
                return Forbid();

            if (string.IsNullOrWhiteSpace(model?.Text))
                return Ok(new { success = false, error = "Message can't be empty." });

            var store = await storeContext.GetCurrentStoreAsync();

            try
            {
                var result = await aiChatService.SendMessageAsync(customer, store.Id, model.Text.Trim(), HttpContext.RequestAborted);
                return Ok(new { success = true, reply = Map(result.AssistantMessage) });
            }
            catch (Exception)
            {
                // The model call (KubeAI) can time out or the gateway can be briefly
                // unavailable - surface a normal chat-bubble-friendly failure instead of a
                // raw 500, the mobile screen has nowhere good to render that.
                return Ok(new
                {
                    success = false,
                    error = "The assistant is temporarily unavailable - please try again in a moment."
                });
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            if (!await IsEntitledAsync(customer))
                return Forbid();

            var store = await storeContext.GetCurrentStoreAsync();
            await conversationService.StartNewConversationAsync(customer.Id, store.Id);

            return Ok(new { success = true });
        }

        private async Task<bool> IsEntitledAsync(Nop.Core.Domain.Customers.Customer customer) =>
            await customerService.IsInCustomerRoleAsync(customer, AiChatRoles.BetaTesterSystemName);

        private static AiChatMessageV2Model Map(Domain.AiChatMessage message) => new()
        {
            Id = message.Id,
            Role = message.IsFromCustomer ? "user" : "ai",
            Text = message.Body,
            ProductIds = string.IsNullOrEmpty(message.SuggestedProductIdsCsv)
                ? new List<int>()
                : message.SuggestedProductIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .ToList(),
            CreatedOnUtc = message.CreatedOnUtc
        };
    }
}
