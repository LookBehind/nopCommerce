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
    /// Self-contained (no dependency on Notifications.Manager). Supports NATIVE function/tool-calling:
    /// the gateway runs vLLM with <c>--enable-auto-tool-choice --tool-call-parser=qwen3_coder</c>, so a
    /// request carrying <c>tools</c> comes back with structured <c>message.tool_calls</c> (and the
    /// reasoning in <c>message.reasoning</c>) rather than tool-call markup leaking into the content.
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

            // Assistant messages that call tools carry the calls; echoing them back (with the tool results
            // as role:"tool" messages) is required by the OpenAI/vLLM tool-calling protocol.
            [JsonPropertyName("tool_calls")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<LlmToolCall> ToolCalls { get; set; }

            // role:"tool" result plumbing.
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

            /// <summary>Arguments as a JSON string (per the OpenAI schema), e.g. <c>{"days":7}</c>.</summary>
            [JsonPropertyName("arguments")]
            public string Arguments { get; set; }
        }

        /// <summary>A tool the model may call. <see cref="LlmFunctionDef.Parameters"/> is a JSON-schema object.</summary>
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

        /// <summary>Result of a tool-enabled completion: the model either called tools OR returned an answer.</summary>
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

            // Populated (and content null) when the model invokes tools — the qwen3_coder parser turns the
            // model's native tool-call markup into this structured form.
            [JsonPropertyName("tool_calls")]
            public List<LlmToolCall> ToolCalls { get; set; }
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
                Messages = messages.ToList()
                // Thinking left ON (model default): richer reasoning. Long turns are safe now — the token cap
                // is removed (the think block can't truncate the answer) and /Chat streams over SSE so slow
                // turns don't hit Cloudflare's ~100s. To disable, set ChatTemplateKwargs { enable_thinking = false }.
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
        /// Posts a chat completion WITH native tool-calling. Returns the model's answer content and/or the
        /// tools it wants to call (structured, parsed by vLLM's qwen3_coder tool-call parser). The caller
        /// runs the tools, appends the results as role:"tool" messages, and calls again until the model
        /// answers with content. Uncapped tokens (reasoning model) bounded by <paramref name="timeout"/>.
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

        /// <summary>
        /// Cheap readiness probe: a 1-token completion under a short timeout. Returns true if the model
        /// answered, false if it timed out or errored. Kept as a health check — the model is pinned
        /// warm (minReplicas 1, no scale-to-zero), so the SPA no longer needs to warm it before chatting.
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
