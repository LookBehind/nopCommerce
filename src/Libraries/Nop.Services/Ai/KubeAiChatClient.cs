using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Services.Ai
{
    /// <inheritdoc cref="IKubeAiChatClient"/>
    public class KubeAiChatClient : IKubeAiChatClient
    {
        /// <summary>
        /// In-cluster KubeAI gateway address (see gpu-mgmt/kubeai in the infra repo). This is an
        /// internal service URL, not merchant-facing, so it is a constant rather than an
        /// admin-configurable setting.
        /// </summary>
        public const string BaseUrl = "http://kubeai.gpu-mgmt.svc.cluster.local/openai/v1/";

        /// <summary>
        /// Default model served by KubeAI (see the Model CRs in gpu-mgmt). Every per-use-case model
        /// id in <see cref="AiSettings"/> defaults to this - admin-configurable from there, not here.
        /// </summary>
        public const string DefaultModel = "qwen3-8-27b-awq";

        private readonly HttpClient _httpClient;

        public KubeAiChatClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
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

            // Omit entirely when null so vLLM lets the model generate up to its context limit. A fixed cap
            // truncates unpredictable reasoning-model "thinking" mid-stream, which returns EMPTY content
            // (finish_reason=length). Callers that want cost/latency control pass an explicit value instead.
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

            // Reasoning models (Qwen3) return their <think> block here, separate from the answer.
            [JsonPropertyName("reasoning")]
            public string Reasoning { get; set; }

            // Populated (and content null) when the model invokes tools.
            [JsonPropertyName("tool_calls")]
            public List<LlmToolCall> ToolCalls { get; set; }
        }

        private class CompletionResponse
        {
            [JsonPropertyName("choices")]
            public List<CompletionChoice> Choices { get; set; }
        }

        public async Task<string> GetChatCompletionAsync(string model, string systemPrompt, string userPrompt,
            TimeSpan timeout, CancellationToken cancellationToken = default, int? maxTokens = null)
        {
            var messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            return await CompleteAsync(model, messages, temperature: 0.0, maxTokens, timeout, cancellationToken);
        }

        public async Task<string> CompleteAsync(
            string model,
            IEnumerable<LlmMessage> messages,
            double temperature,
            int? maxTokens,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var request = new CompletionRequest
            {
                Model = model,
                Stream = false,
                Temperature = temperature,
                MaxTokens = maxTokens,
                Messages = messages.ToList()
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, cts.Token);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<CompletionResponse>(cancellationToken: cts.Token);
            var choice = parsed?.Choices?.FirstOrDefault();
            var content = choice?.Message?.Content;
            if (!string.IsNullOrWhiteSpace(content))
                return content;

            // Empty content: with the cap removed this should be rare. If it still happens because the model
            // hit its context limit while thinking, say so specifically instead of a generic failure.
            if (string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("KubeAI chat completion was truncated (hit the context limit while reasoning) and returned no answer.");
            throw new InvalidOperationException("KubeAI chat completion returned no content");
        }

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

        public async Task<bool> IsReadyAsync(string model, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new CompletionRequest
                {
                    Model = model,
                    Stream = false,
                    Temperature = 0.0,
                    MaxTokens = 1,
                    Messages = new List<LlmMessage> { new() { Role = "user", Content = "ping" } }
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
