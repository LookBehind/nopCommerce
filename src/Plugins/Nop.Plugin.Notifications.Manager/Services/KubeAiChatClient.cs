using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Notifications.Manager.Services
{
    /// <summary>
    /// Represents the HTTP client to request chat completions from the self-hosted KubeAI/vLLM
    /// gateway (OpenAI-compatible), used for the RemindMe recommendation prompt
    /// </summary>
    public class KubeAiChatClient
    {
        #region Fields

        /// <summary>
        /// In-cluster KubeAI gateway address (see gpu-mgmt/kubeai in the infra repo). This is an
        /// internal service URL, not merchant-facing, so it is a constant rather than an
        /// admin-configurable setting - same treatment the Ollama endpoint it replaces had.
        /// </summary>
        public const string BaseUrl = "http://kubeai.gpu-mgmt.svc.cluster.local/openai/v1/";

        private readonly HttpClient _httpClient;

        #endregion

        #region Ctor

        public KubeAiChatClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        #endregion

        #region Nested request/response types

        private class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class ChatCompletionRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public List<ChatMessage> Messages { get; set; }

            [JsonPropertyName("stream")]
            public bool Stream { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }

            [JsonPropertyName("max_tokens")]
            public int? MaxTokens { get; set; }
        }

        private class ChatCompletionChoice
        {
            [JsonPropertyName("message")]
            public ChatMessage Message { get; set; }
        }

        private class ChatCompletionResponse
        {
            [JsonPropertyName("choices")]
            public List<ChatCompletionChoice> Choices { get; set; }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Posts a chat completion request and returns the raw assistant content string. Throws
        /// on any HTTP error, timeout, or missing content - callers are expected to catch and
        /// fall back.
        /// </summary>
        public async Task<string> GetChatCompletionAsync(string model, string systemPrompt, string userPrompt,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var request = new ChatCompletionRequest
            {
                Model = model,
                Stream = false,
                Temperature = 0.0,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, cts.Token);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cts.Token);
            return parsed?.Choices?.FirstOrDefault()?.Message?.Content
                ?? throw new InvalidOperationException("KubeAI chat completion returned no content");
        }

        /// <summary>
        /// Cheap readiness probe (a minimal 1-token completion). Never throws - returns false on
        /// any failure, including the model still being scaled to zero / mid cold-start, so
        /// callers can poll this in a loop without their own try/catch.
        /// </summary>
        public async Task<bool> IsReadyAsync(string model, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var request = new ChatCompletionRequest
            {
                Model = model,
                Stream = false,
                Temperature = 0.0,
                MaxTokens = 1,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "ping" }
                }
            };

            try
            {
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

        #endregion
    }
}
