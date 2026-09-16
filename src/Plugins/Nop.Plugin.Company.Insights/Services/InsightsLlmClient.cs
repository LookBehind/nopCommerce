using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Minimal OpenAI-compatible chat-completions client for the self-hosted KubeAI/vLLM gateway.
    /// Self-contained (no dependency on Notifications.Manager). The agent uses a prompt-based
    /// JSON action protocol rather than native tool-calling, so this only needs plain completions.
    /// </summary>
    public class InsightsLlmClient
    {
        /// <summary>In-cluster KubeAI gateway (gpu-mgmt/kubeai). Internal, not admin-configurable.</summary>
        public const string BaseUrl = "http://kubeai.gpu-mgmt.svc.cluster.local/openai/v1/";

        /// <summary>Default model served by KubeAI (see the Model CRs in gpu-mgmt).</summary>
        public const string DefaultModel = "qwen3-8-27b-awq";

        private readonly HttpClient _httpClient;

        public InsightsLlmClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public class LlmMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
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
            // (finish_reason=length). We bound cost by wall-clock time instead, never by a token count.
            [JsonPropertyName("max_tokens")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public int? MaxTokens { get; set; }

            // Qwen3 chat-template controls. We disable the <think> block for this agent: it uses a strict
            // JSON tool protocol that doesn't need chain-of-thought, and thinking burns huge token budgets
            // (slow on the self-hosted GPU, and it truncated to empty content). Omitted when null.
            [JsonPropertyName("chat_template_kwargs")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, object> ChatTemplateKwargs { get; set; }
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
        }

        private class CompletionResponse
        {
            [JsonPropertyName("choices")]
            public List<CompletionChoice> Choices { get; set; }
        }

        /// <summary>
        /// Posts a chat completion and returns the assistant content. Pass maxTokens=null (the default for
        /// the agent) to leave the completion uncapped so a reasoning model can finish thinking AND answer;
        /// a cap that truncates the think block yields empty content. Throws on HTTP error/timeout/empty.
        /// </summary>
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
                Messages = messages.ToList(),
                ChatTemplateKwargs = new Dictionary<string, object> { ["enable_thinking"] = false }
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

        /// <summary>
        /// Cheap readiness probe: a 1-token completion under a short timeout. Returns true if the model
        /// answered (warm), false if it timed out or errored (still scaling up). Sending it also nudges
        /// KubeAI to scale the model from zero, so the SPA can poll this to wake the model without
        /// holding a long request open through the CDN.
        /// </summary>
        public async Task<bool> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new CompletionRequest
                {
                    Model = DefaultModel,
                    Stream = false,
                    Temperature = 0.0,
                    MaxTokens = 1,
                    Messages = new List<LlmMessage> { new LlmMessage { Role = "user", Content = "ping" } }
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
