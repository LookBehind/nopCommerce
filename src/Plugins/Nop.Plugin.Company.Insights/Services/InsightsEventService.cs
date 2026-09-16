using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Npgsql;
using NpgsqlTypes;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Postgres-backed agent-event outbox. Raw Npgsql, lazy schema — mirrors the other Insights
    /// stores. Enqueue is best-effort and swallows all errors so an event consumer can never break
    /// the request (order placement, review save) that produced the event.
    /// </summary>
    public class InsightsEventService : IInsightsEventService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;

        private readonly InsightsMemoryConfig _config;
        private readonly ILogger _logger;

        public InsightsEventService(InsightsMemoryConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool Enabled => _config.Enabled;

        public async Task<long> EnqueueAsync(string eventType, string entityType, int? entityId, int? companyId, string payloadJson, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(eventType))
                return 0;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                long id;
                await using (var conn = new NpgsqlConnection(_config.BuildConnectionString()))
                {
                    await conn.OpenAsync(cancellationToken);
                    await using var cmd = new NpgsqlCommand(
                        "INSERT INTO insights_agent_event (tenant, event_type, entity_type, entity_id, company_id, payload, status) " +
                        "VALUES (@tenant, @type, @etype, @eid, @cid, @payload::jsonb, 'new') RETURNING id", conn);
                    cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                    cmd.Parameters.AddWithValue("type", eventType);
                    cmd.Parameters.AddWithValue("etype", (object)entityType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("eid", (object)entityId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cid", (object)companyId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
                    id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
                }

                // Dispatch off the request thread. Static Hangfire API — each tenant process has its own storage.
                try { BackgroundJob.Enqueue<IInsightsEventDispatcher>(d => d.DispatchAsync(id)); }
                catch (Exception ex) { await _logger.WarningAsync("Insights events: dispatch enqueue failed", ex); }

                return id;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights events: enqueue failed", ex);
                return 0;
            }
        }

        public async Task<long> EnqueueUniqueAsync(string eventType, string entityType, int? entityId, int? companyId, string payloadJson, int dedupeWindowHours, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(eventType))
                return 0;

            try
            {
                await EnsureSchemaAsync(cancellationToken);
                long id = 0;
                await using (var conn = new NpgsqlConnection(_config.BuildConnectionString()))
                {
                    await conn.OpenAsync(cancellationToken);
                    await using var cmd = new NpgsqlCommand(
                        "INSERT INTO insights_agent_event (tenant, event_type, entity_type, entity_id, company_id, payload, status) " +
                        "SELECT @tenant, @type, @etype, @eid, @cid, @payload::jsonb, 'new' " +
                        "WHERE NOT EXISTS (SELECT 1 FROM insights_agent_event WHERE tenant = @tenant AND event_type = @type " +
                        "  AND entity_id IS NOT DISTINCT FROM @eid AND occurred_at > now() - make_interval(hours => @win)) " +
                        "RETURNING id", conn);
                    cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                    cmd.Parameters.AddWithValue("type", eventType);
                    cmd.Parameters.AddWithValue("etype", (object)entityType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("eid", (object)entityId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cid", (object)companyId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
                    cmd.Parameters.AddWithValue("win", Math.Max(1, dedupeWindowHours));
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (scalar != null && scalar != DBNull.Value)
                        id = Convert.ToInt64(scalar);
                }

                if (id > 0)
                {
                    try { BackgroundJob.Enqueue<IInsightsEventDispatcher>(d => d.DispatchAsync(id)); }
                    catch (Exception ex) { await _logger.WarningAsync("Insights events: dispatch enqueue failed", ex); }
                }
                return id;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights events: unique enqueue failed", ex);
                return 0;
            }
        }

        public async Task<InsightsAgentEvent> GetAsync(long id, CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return null;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT id, event_type, entity_type, entity_id, company_id, payload::text, status, occurred_at, processed_at " +
                    "FROM insights_agent_event WHERE tenant = @tenant AND id = @id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("id", id);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights events: get failed", ex);
                return null;
            }
        }

        public async Task MarkProcessedAsync(long id, CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return;
            try
            {
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "UPDATE insights_agent_event SET status = 'processed', processed_at = now() WHERE tenant = @tenant AND id = @id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights events: mark processed failed", ex);
            }
        }

        public async Task<IList<InsightsAgentEvent>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
        {
            var list = new List<InsightsAgentEvent>();
            if (!Enabled)
                return list;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT id, event_type, entity_type, entity_id, company_id, payload::text, status, occurred_at, processed_at " +
                    "FROM insights_agent_event WHERE tenant = @tenant ORDER BY id DESC LIMIT @limit", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    list.Add(Map(reader));
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights events: list failed", ex);
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
                    "DELETE FROM insights_agent_event WHERE tenant = @tenant AND status = 'processed' " +
                    "AND occurred_at < now() - make_interval(days => @days)", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("days", Math.Max(1, days));
                return await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights events: purge failed", ex);
                return 0;
            }
        }

        private static InsightsAgentEvent Map(NpgsqlDataReader r) => new InsightsAgentEvent
        {
            Id = r.GetInt64(0),
            EventType = r.GetString(1),
            EntityType = r.IsDBNull(2) ? null : r.GetString(2),
            EntityId = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
            CompanyId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
            Payload = r.IsDBNull(5) ? null : r.GetString(5),
            Status = r.GetString(6),
            OccurredAt = r.GetDateTime(7),
            ProcessedAt = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8)
        };

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
                    "CREATE TABLE IF NOT EXISTS insights_agent_event (" +
                    "  id bigserial PRIMARY KEY," +
                    "  tenant text NOT NULL," +
                    "  event_type text NOT NULL," +
                    "  entity_type text," +
                    "  entity_id integer," +
                    "  company_id integer," +
                    "  payload jsonb," +
                    "  status text NOT NULL DEFAULT 'new'," +
                    "  occurred_at timestamptz NOT NULL DEFAULT now()," +
                    "  processed_at timestamptz);" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_agent_event_tenant ON insights_agent_event (tenant, occurred_at DESC);" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_agent_event_status ON insights_agent_event (tenant, status);";
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
