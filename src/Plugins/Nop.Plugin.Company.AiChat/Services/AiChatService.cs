using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Company.AiChat.Domain;

namespace Nop.Plugin.Company.AiChat.Services
{
    /// <summary>
    /// Orchestrates one AI Assistant turn: persists the customer's message, calls the
    /// self-hosted Qwen3 model (letting it call search_products against the real, allergy-
    /// filtered catalog rather than inventing menu items), and persists+returns the reply.
    /// </summary>
    public partial class AiChatService : IAiChatService
    {
        private const string SearchProductsToolName = "search_products";
        private const int MaxHistoryMessages = 20;
        private const int MaxToolRounds = 3;
        private const int MaxSuggestedProducts = 6;

        private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(75);

        private readonly IAiChatConversationService _conversationService;
        private readonly IAiChatCatalogService _catalogService;
        private readonly AiChatLlmClient _llmClient;

        public AiChatService(
            IAiChatConversationService conversationService,
            IAiChatCatalogService catalogService,
            AiChatLlmClient llmClient)
        {
            _conversationService = conversationService;
            _catalogService = catalogService;
            _llmClient = llmClient;
        }

        public virtual async Task<AiChatTurnResult> SendMessageAsync(Customer customer, int storeId, string userText, CancellationToken cancellationToken = default)
        {
            var conversation = await _conversationService.GetOrCreateCurrentConversationAsync(customer.Id, storeId);
            await _conversationService.AddMessageAsync(conversation.Id, isFromCustomer: true, userText);

            var history = await _conversationService.GetMessagesAsync(conversation.Id);
            var recentHistory = history
                .OrderByDescending(m => m.CreatedOnUtc)
                .Take(MaxHistoryMessages)
                .OrderBy(m => m.CreatedOnUtc)
                .ToList();

            var allergies = await _catalogService.GetCustomerAllergiesAsync(customer, storeId);

            var messages = new List<AiChatLlmClient.LlmMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt(allergies) }
            };
            messages.AddRange(recentHistory.Select(m => new AiChatLlmClient.LlmMessage
            {
                Role = m.IsFromCustomer ? "user" : "assistant",
                Content = m.Body
            }));

            var tools = new List<AiChatLlmClient.LlmTool> { BuildSearchProductsTool() };

            // Accumulated across every search_products call this turn (a reasoning model
            // routinely searches more than once per reply - e.g. once for a light option and
            // again for a heartier one) - showing only the LAST call's results would drop
            // products the model's own answer text already named.
            var seenProductIds = new List<int>();
            string finalContent = null;

            for (var round = 0; round < MaxToolRounds; round++)
            {
                var completion = await _llmClient.CompleteWithToolsAsync(
                    AiChatLlmClient.DefaultModel,
                    messages,
                    temperature: 0.5,
                    tools: tools,
                    timeout: CompletionTimeout,
                    cancellationToken: cancellationToken);

                if (!completion.HasToolCalls)
                {
                    finalContent = completion.Content;
                    break;
                }

                messages.Add(new AiChatLlmClient.LlmMessage { Role = "assistant", ToolCalls = completion.ToolCalls.ToList() });

                foreach (var toolCall in completion.ToolCalls)
                {
                    var query = ExtractQueryArgument(toolCall.Function?.Arguments);
                    var candidates = await _catalogService.SearchAsync(customer, storeId, query, MaxSuggestedProducts);

                    foreach (var id in candidates.Select(c => c.Id))
                        if (!seenProductIds.Contains(id))
                            seenProductIds.Add(id);

                    messages.Add(new AiChatLlmClient.LlmMessage
                    {
                        Role = "tool",
                        ToolCallId = toolCall.Id,
                        Name = toolCall.Function?.Name,
                        Content = JsonSerializer.Serialize(candidates.Select(c => new
                        {
                            id = c.Id,
                            name = c.Name,
                            price = c.Price,
                            vendor = c.VendorName,
                            category = c.CategoryName,
                            ingredients = c.IngredientLabels
                        }))
                    });
                }
            }

            finalContent ??= "Sorry, I'm having trouble finding an answer right now - could you try rephrasing that?";

            // Cap the CARDS shown below the reply, not the pool the model searched over -
            // MaxSuggestedProducts already bounds each individual search_products call.
            var suggestedProductIds = seenProductIds.Take(MaxSuggestedProducts * 2).ToList();

            var assistantMessage = await _conversationService.AddMessageAsync(
                conversation.Id, isFromCustomer: false, finalContent, suggestedProductIds);

            return new AiChatTurnResult
            {
                AssistantMessage = assistantMessage,
                SuggestedProductIds = suggestedProductIds
            };
        }

        private static string ExtractQueryArgument(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                if (doc.RootElement.TryGetProperty("query", out var queryProp))
                    return queryProp.GetString();
            }
            catch (JsonException)
            {
                // Fall through - a malformed/empty arguments blob just means "no query filter".
            }

            return null;
        }

        private static AiChatLlmClient.LlmTool BuildSearchProductsTool() => new()
        {
            Function = new AiChatLlmClient.LlmFunctionDef
            {
                Name = SearchProductsToolName,
                Description = "Searches the real MySnacks catalog (across all vendors) for purchasable products matching a short keyword query, e.g. a dish, cuisine, ingredient, or dietary need. Results already exclude anything conflicting with the customer's saved allergies. Always call this before recommending specific products - never invent a product, price, or vendor.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "Short keyword search, e.g. 'vegan', 'high protein', 'kebab', 'dessert'. Leave empty to just browse popular items."
                        }
                    },
                    required = Array.Empty<string>()
                }
            }
        };

        private static string BuildSystemPrompt(IList<string> allergies)
        {
            var allergyNote = allergies is { Count: > 0 }
                ? $"The customer has already told us they're allergic to: {string.Join(", ", allergies)}. search_products automatically excludes matching items, so you don't need to filter results yourself - just don't second-guess or re-suggest something the customer confirms was excluded."
                : "The customer hasn't recorded any allergies yet.";

            return
                "You are the MySnacks AI meal assistant, built into the mobile app. Customers order food for delivery " +
                "from several local vendors on the platform. Help them decide what to eat, answer questions about " +
                "ingredients or dietary needs, and suggest real products.\n\n" +
                "You do NOT know today's menu from memory. Always call search_products with a short keyword query " +
                "before recommending anything by name - never invent a product, price, or vendor. If a search turns " +
                "up nothing useful, say so honestly instead of making something up.\n\n" +
                allergyNote + "\n\n" +
                "Keep replies short and conversational (2-4 sentences). You're recommending real food from a real " +
                "menu, not writing a long essay. Plain text only - the mobile app renders your reply as-is with no " +
                "markdown support, so never use **bold**, bullet points, or headings.";
        }
    }
}
