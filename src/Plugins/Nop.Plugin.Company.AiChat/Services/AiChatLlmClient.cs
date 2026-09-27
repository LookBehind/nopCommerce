using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.AiChat.Services
{
    /// <summary>
    /// Minimal OpenAI-compatible chat-completions client for the self-hosted KubeAI/vLLM gateway.
    /// Self-contained (no dependency on any other plugin) - mirrors
    /// Nop.Plugin.Company.Insights.Services.InsightsLlmClient exactly (same gateway, same model,
    /// same tool-calling shape); duplicated rather than shared because no plugin in this codebase
    /// takes a project reference on another plugin.
    /// </summary>
    public class AiChatLlmClient
    {
        /// <summary>In-cluster KubeAI gateway (gpu-mgmt/kubeai). Internal, not admin-configurable.</summary>
        public const string BaseUrl = "http://kubeai.gpu-mgmt.svc.cluster.local/openai/v1/";

        /// <summary>Default model served by KubeAI (see the Model CRs in gpu-mgmt).</summary>
        public const string DefaultModel = "qwen3-8-27b-awq";

        private readonly HttpClient _httpClient;

        public AiChatLlmClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public class LlmMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }

            [JsonPropertyName("tool_calls")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<LlmToolCall> ToolCalls { get; set; }

            [JsonPropertyName("tool_call_id")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string ToolCallId { get; set; }

            [JsonPropertyName("name")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Name { get; set; }
        }

        public class LlmToolCall
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; } = "function";

            [JsonPropertyName("function")]
            public LlmFunctionCall Function { get; set; }
        }

        public class LlmFunctionCall
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            /// <summary>Arguments as a JSON string (per the OpenAI schema), e.g. <c>{"query":"vegan"}</c>.</summary>
            [JsonPropertyName("arguments")]
            public string Arguments { get; set; }
        }

        public class LlmTool
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "function";

            [JsonPropertyName("function")]
            public LlmFunctionDef Function { get; set; }
        }

        public class LlmFunctionDef
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("parameters")]
            public object Parameters { get; set; }
        }

        public class LlmCompletion
        {
            public string Content { get; set; }
            public IList<LlmToolCall> ToolCalls { get; set; }
            public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
        }

        private class CompletionRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public List<LlmMessage> Messages { get; set; }

            [JsonPropertyName("stream")]
            public bool Stream { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }

            // Omit entirely when null so vLLM lets the model generate up to its context limit. A
            // fixed cap truncates unpredictable reasoning-model "thinking" mid-stream, which
            // returns EMPTY content (finish_reason=length) - see InsightsLlmClient, the same
            // gotcha was found there first. We bound cost by wall-clock time instead.
            [JsonPropertyName("max_tokens")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public int? MaxTokens { get; set; }

            [JsonPropertyName("tools")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<LlmTool> Tools { get; set; }

            [JsonPropertyName("tool_choice")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string ToolChoice { get; set; }
        }

        private class CompletionChoice
        {
            [JsonPropertyName("message")]
            public ChoiceMessage Message { get; set; }

            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; }
        }

        private class ChoiceMessage
        {
            [JsonPropertyName("content")]
            public string Content { get; set; }

            [JsonPropertyName("reasoning")]
            public string Reasoning { get; set; }

            [JsonPropertyName("tool_calls")]
            public List<LlmToolCall> ToolCalls { get; set; }
        }

        private class CompletionResponse
        {
            [JsonPropertyName("choices")]
            public List<CompletionChoice> Choices { get; set; }
        }

        /// <summary>
        /// Posts a chat completion WITH native tool-calling. Returns the model's answer content
        /// and/or the tools it wants to call. The caller runs the tools, appends the results as
        /// role:"tool" messages, and calls again until the model answers with content.
        /// </summary>
        public async Task<LlmCompletion> CompleteWithToolsAsync(
            string model,
            IEnumerable<LlmMessage> messages,
            double temperature,
            IEnumerable<LlmTool> tools,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var toolList = tools?.ToList();
            var request = new CompletionRequest
            {
                Model = model,
                Stream = false,
                Temperature = temperature,
                Messages = messages.ToList(),
                Tools = toolList != null && toolList.Count > 0 ? toolList : null,
                ToolChoice = toolList != null && toolList.Count > 0 ? "auto" : null
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, cts.Token);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<CompletionResponse>(cancellationToken: cts.Token);
            var choice = parsed?.Choices?.FirstOrDefault();
            var message = choice?.Message;
            var result = new LlmCompletion { Content = message?.Content, ToolCalls = message?.ToolCalls };

            if (result.HasToolCalls || !string.IsNullOrWhiteSpace(result.Content))
                return result;

            if (string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("KubeAI chat completion was truncated (hit the context limit while reasoning) and returned no answer.");
            throw new InvalidOperationException("KubeAI chat completion returned neither content nor a tool call");
        }
    }
}
