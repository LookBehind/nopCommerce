using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// OpenAI-compatible embeddings client for the in-cluster CPU embedder (Ollama/bge-m3).
    /// </summary>
    public class InsightsEmbedderClient
    {
        private readonly HttpClient _httpClient;
        private readonly InsightsMemoryConfig _config;

        public InsightsEmbedderClient(HttpClient httpClient, InsightsMemoryConfig config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        private class EmbeddingRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("input")]
            public string Input { get; set; }
        }

        private class EmbeddingData
        {
            [JsonPropertyName("embedding")]
            public float[] Embedding { get; set; }
        }

        private class EmbeddingResponse
        {
            [JsonPropertyName("data")]
            public List<EmbeddingData> Data { get; set; }
        }

        /// <summary>Embeds a single string. Throws on failure — callers decide whether to degrade.</summary>
        public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            var request = new EmbeddingRequest { Model = _config.EmbedderModel, Input = text ?? string.Empty };

            using var response = await _httpClient.PostAsJsonAsync(_config.EmbedderUrl, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken);
            var embedding = parsed?.Data is { Count: > 0 } ? parsed.Data[0].Embedding : null;
            if (embedding == null || embedding.Length == 0)
                throw new InvalidOperationException("Embedder returned no vector");
            return embedding;
        }
    }
}
