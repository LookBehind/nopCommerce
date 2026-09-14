using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Postgres-backed per-user persistence for the workspace blob and chat conversations. Raw Npgsql
    /// (no ORM), schema ensured lazily on first use — mirrors <see cref="InsightsScheduleService"/>.
    /// All methods are best-effort: they log and degrade so a store outage can't break the SPA.
    /// </summary>
    public class InsightsWorkspaceService : IInsightsWorkspaceService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;

        private readonly InsightsMemoryConfig _config;
        private readonly ILogger _logger;

        public InsightsWorkspaceService(InsightsMemoryConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool Enabled => _config.Enabled;

        public async Task<string> GetWorkspaceAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return null;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT data::text FROM insights_workspace WHERE tenant = @tenant AND user_id = @uid", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("uid", userId);
                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                return result as string;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights workspace: load failed", ex);
                return null;
            }
        }

        public async Task<bool> SaveWorkspaceAsync(int userId, string dataJson, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(dataJson))
                return false;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO insights_workspace (tenant, user_id, data, updated_at) " +
                    "VALUES (@tenant, @uid, @data::jsonb, now()) " +
                    "ON CONFLICT (tenant, user_id) DO UPDATE SET data = EXCLUDED.data, updated_at = now()", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("data", NpgsqlDbType.Text, dataJson);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights workspace: save failed", ex);
                return false;
            }
        }

        public async Task<IList<InsightsConversation>> ListConversationsAsync(int userId, CancellationToken cancellationToken = default)
        {
            var list = new List<InsightsConversation>();
            if (!Enabled)
                return list;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT id, title, agent_id, created_at, updated_at FROM insights_conversation " +
                    "WHERE tenant = @tenant AND user_id = @uid ORDER BY updated_at DESC LIMIT 100", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("uid", userId);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    list.Add(new InsightsConversation
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        AgentId = reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3),
                        UpdatedAt = reader.GetDateTime(4)
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights workspace: list conversations failed", ex);
            }
            return list;
        }

        public async Task<InsightsConversation> GetConversationAsync(int userId, string id, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(id))
                return null;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT id, title, agent_id, messages::text, created_at, updated_at FROM insights_conversation " +
                    "WHERE tenant = @tenant AND user_id = @uid AND id = @id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("id", id);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return null;
                return new InsightsConversation
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    AgentId = reader.GetString(2),
                    MessagesJson = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4),
                    UpdatedAt = reader.GetDateTime(5)
                };
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights workspace: get conversation failed", ex);
                return null;
            }
        }

        public async Task<InsightsConversation> SaveConversationAsync(int userId, InsightsConversation c, CancellationToken cancellationToken = default)
        {
            if (!Enabled || c == null)
                return null;

            try
            {
                await EnsureSchemaAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(c.Id))
                {
                    c.Id = Guid.NewGuid().ToString("N");
                    c.CreatedAt = DateTime.UtcNow;
                }
                else if (c.CreatedAt == default)
                {
                    c.CreatedAt = DateTime.UtcNow;
                }
                c.UpdatedAt = DateTime.UtcNow;

                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO insights_conversation (id, tenant, user_id, title, agent_id, messages, created_at, updated_at) " +
                    "VALUES (@id, @tenant, @uid, @title, @agent, @messages::jsonb, @created, @updated) " +
                    "ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, agent_id = EXCLUDED.agent_id, " +
                    "messages = EXCLUDED.messages, updated_at = EXCLUDED.updated_at", conn);
                cmd.Parameters.AddWithValue("id", c.Id);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("title", string.IsNullOrWhiteSpace(c.Title) ? "Conversation" : c.Title);
                cmd.Parameters.AddWithValue("agent", string.IsNullOrWhiteSpace(c.AgentId) ? "analyst" : c.AgentId);
                cmd.Parameters.AddWithValue("messages", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(c.MessagesJson) ? "[]" : c.MessagesJson);
                cmd.Parameters.AddWithValue("created", DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc));
                cmd.Parameters.AddWithValue("updated", DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                return c;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights workspace: save conversation failed", ex);
                return null;
            }
        }

        public async Task DeleteConversationAsync(int userId, string id, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(id))
                return;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "DELETE FROM insights_conversation WHERE tenant = @tenant AND user_id = @uid AND id = @id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("uid", userId);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights workspace: delete conversation failed", ex);
            }
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
                    "CREATE TABLE IF NOT EXISTS insights_workspace (" +
                    "  tenant text NOT NULL," +
                    "  user_id integer NOT NULL," +
                    "  data jsonb NOT NULL," +
                    "  updated_at timestamptz NOT NULL DEFAULT now()," +
                    "  PRIMARY KEY (tenant, user_id));" +
                    "CREATE TABLE IF NOT EXISTS insights_conversation (" +
                    "  id text PRIMARY KEY," +
                    "  tenant text NOT NULL," +
                    "  user_id integer NOT NULL," +
                    "  title text NOT NULL," +
                    "  agent_id text NOT NULL DEFAULT 'analyst'," +
                    "  messages jsonb NOT NULL DEFAULT '[]'::jsonb," +
                    "  created_at timestamptz NOT NULL DEFAULT now()," +
                    "  updated_at timestamptz NOT NULL DEFAULT now());" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_conversation_user " +
                    "ON insights_conversation (tenant, user_id, updated_at DESC);";
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _schemaReady = true;
            }
            finally
            {
                SchemaLock.Release();
            }
        }
    }
}
