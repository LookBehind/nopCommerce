using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// pgvector-backed agent memory. Uses raw Npgsql (no ORM) with the embedding passed as a
    /// pgvector text literal cast to <c>vector</c>. Schema is ensured lazily on first use.
    /// </summary>
    public class InsightsMemoryService : IInsightsMemoryService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;

        private readonly InsightsMemoryConfig _config;
        private readonly InsightsEmbedderClient _embedder;
        private readonly ILogger _logger;

        public InsightsMemoryService(InsightsMemoryConfig config, InsightsEmbedderClient embedder, ILogger logger)
        {
            _config = config;
            _embedder = embedder;
            _logger = logger;
        }

        public bool Enabled => _config.Enabled;

        public async Task<bool> RememberAsync(string agentId, string kind, string content, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(content))
                return false;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                var embedding = await _embedder.EmbedAsync(content, cancellationToken);

                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO insights_memory (tenant, agent_id, kind, content, embedding) " +
                    "VALUES (@tenant, @agent, @kind, @content, @emb::vector)", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("agent", agentId ?? "analyst");
                cmd.Parameters.AddWithValue("kind", string.IsNullOrWhiteSpace(kind) ? "note" : kind);
                cmd.Parameters.AddWithValue("content", content);
                cmd.Parameters.AddWithValue("emb", VectorLiteral(embedding));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights memory: remember failed", ex);
                return false;
            }
        }

        public async Task<IList<MemoryItem>> RecallAsync(string agentId, string query, int k, CancellationToken cancellationToken = default)
        {
            var results = new List<MemoryItem>();
            if (!Enabled || string.IsNullOrWhiteSpace(query))
                return results;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                var embedding = await _embedder.EmbedAsync(query, cancellationToken);

                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT content, kind, created_at, embedding <=> @emb::vector AS distance " +
                    "FROM insights_memory WHERE tenant = @tenant AND agent_id = @agent " +
                    "ORDER BY embedding <=> @emb::vector LIMIT @k", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("agent", agentId ?? "analyst");
                cmd.Parameters.AddWithValue("emb", VectorLiteral(embedding));
                cmd.Parameters.AddWithValue("k", Math.Clamp(k, 1, 20));

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new MemoryItem
                    {
                        Content = reader.GetString(0),
                        Kind = reader.GetString(1),
                        CreatedAt = reader.GetDateTime(2),
                        Distance = reader.GetDouble(3)
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights memory: recall failed", ex);
            }
            return results;
        }

        private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
        {
            if (_schemaReady)
                return;

            await SchemaLock.WaitAsync(cancellationToken);
            try
            {
                if (_schemaReady)
                    return;

                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                var ddl =
                    "CREATE TABLE IF NOT EXISTS insights_memory (" +
                    "  id bigserial PRIMARY KEY," +
                    "  tenant text NOT NULL," +
                    "  agent_id text NOT NULL," +
                    "  kind text NOT NULL DEFAULT 'note'," +
                    "  content text NOT NULL," +
                    $" embedding vector({_config.EmbeddingDim}) NOT NULL," +
                    "  created_at timestamptz NOT NULL DEFAULT now());" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_memory_lookup ON insights_memory (tenant, agent_id);";
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _schemaReady = true;
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        private static string VectorLiteral(IEnumerable<float> embedding)
        {
            var sb = new StringBuilder("[");
            var first = true;
            foreach (var f in embedding)
            {
                if (!first) sb.Append(',');
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
