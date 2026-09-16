using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    public class InsightsAgentRunService : IInsightsAgentRunService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;

        private readonly InsightsMemoryConfig _config;
        private readonly ILogger _logger;

        public InsightsAgentRunService(InsightsMemoryConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool Enabled => _config.Enabled;

        public async Task<long> StartAsync(string agentId, string agentName, long? eventId, string triggerType, string inputJson, CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return 0;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO insights_agent_run (tenant, agent_id, agent_name, event_id, trigger_type, input, status, started_at) " +
                    "VALUES (@tenant, @aid, @aname, @eid, @ttype, @input::jsonb, 'running', now()) RETURNING id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("aid", agentId ?? "");
                cmd.Parameters.AddWithValue("aname", (object)agentName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("eid", (object)eventId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("ttype", (object)triggerType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("input", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights agent-run: start failed", ex);
                return 0;
            }
        }

        public async Task FinishAsync(long runId, string status, string outputJson, string error, CancellationToken cancellationToken = default)
        {
            if (!Enabled || runId <= 0)
                return;
            try
            {
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "UPDATE insights_agent_run SET status = @status, output = @output::jsonb, error = @error, " +
                    "finished_at = now(), duration_ms = CAST(EXTRACT(EPOCH FROM (now() - started_at)) * 1000 AS integer) " +
                    "WHERE tenant = @tenant AND id = @id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("id", runId);
                cmd.Parameters.AddWithValue("status", status ?? "ok");
                cmd.Parameters.AddWithValue("output", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(outputJson) ? "{}" : outputJson);
                cmd.Parameters.AddWithValue("error", (object)error ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights agent-run: finish failed", ex);
            }
        }

        public async Task<IList<InsightsAgentRun>> ListRecentAsync(int limit, string agentId, CancellationToken cancellationToken = default)
        {
            var list = new List<InsightsAgentRun>();
            if (!Enabled)
                return list;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                var sql = "SELECT id, agent_id, agent_name, event_id, trigger_type, input::text, started_at, finished_at, duration_ms, status, output::text, error " +
                          "FROM insights_agent_run WHERE tenant = @tenant" + (string.IsNullOrWhiteSpace(agentId) ? "" : " AND agent_id = @aid") +
                          " ORDER BY id DESC LIMIT @limit";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
                if (!string.IsNullOrWhiteSpace(agentId))
                    cmd.Parameters.AddWithValue("aid", agentId);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    list.Add(new InsightsAgentRun
                    {
                        Id = reader.GetInt64(0),
                        AgentId = reader.GetString(1),
                        AgentName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        EventId = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3),
                        TriggerType = reader.IsDBNull(4) ? null : reader.GetString(4),
                        InputJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                        StartedAt = reader.GetDateTime(6),
                        FinishedAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                        DurationMs = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8),
                        Status = reader.GetString(9),
                        OutputJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Error = reader.IsDBNull(11) ? null : reader.GetString(11)
                    });
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights agent-run: list failed", ex);
            }
            return list;
        }

        public async Task<int> PurgeOlderThanAsync(int days, CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return 0;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "DELETE FROM insights_agent_run WHERE tenant = @tenant AND status <> 'running' " +
                    "AND started_at < now() - make_interval(days => @days)", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("days", Math.Max(1, days));
                return await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights agent-run: purge failed", ex);
                return 0;
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
                    "CREATE TABLE IF NOT EXISTS insights_agent_run (" +
                    "  id bigserial PRIMARY KEY, tenant text NOT NULL, agent_id text NOT NULL, agent_name text," +
                    "  event_id bigint, trigger_type text, input jsonb," +
                    "  started_at timestamptz NOT NULL DEFAULT now(), finished_at timestamptz, duration_ms integer," +
                    "  status text NOT NULL DEFAULT 'running', output jsonb, error text);" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_agent_run_tenant ON insights_agent_run (tenant, id DESC);";
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
