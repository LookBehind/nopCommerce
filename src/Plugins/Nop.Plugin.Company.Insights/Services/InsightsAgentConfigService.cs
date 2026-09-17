using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Npgsql;
using NpgsqlTypes;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    public class InsightsAgentConfigService : IInsightsAgentConfigService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;
        private static readonly TimeZoneInfo Tz = ResolveTz();

        private readonly InsightsMemoryConfig _config;
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly ILogger _logger;

        public InsightsAgentConfigService(InsightsMemoryConfig config, IRecurringJobManager recurringJobManager, ILogger logger)
        {
            _config = config;
            _recurringJobManager = recurringJobManager;
            _logger = logger;
        }

        public bool Enabled => _config.Enabled;

        private const string Cols =
            "id, name, enabled, built_in, owner_user_id, company_id, trigger_kind, event_type, cron, " +
            "filter::text, system_prompt, instruction, output_sinks::text, output_target, created_at, updated_at";

        public async Task<IList<InsightsAgentConfig>> ListAsync(CancellationToken cancellationToken = default)
        {
            var list = new List<InsightsAgentConfig>();
            if (!Enabled)
                return list;
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand($"SELECT {Cols} FROM insights_agent WHERE tenant = @tenant ORDER BY created_at", conn);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                list.Add(Map(reader));
            return list;
        }

        public async Task<InsightsAgentConfig> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(id))
                return null;
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand($"SELECT {Cols} FROM insights_agent WHERE tenant = @tenant AND id = @id", conn);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
        }

        public async Task<IList<InsightsAgentConfig>> GetEnabledForEventAsync(string eventType, IList<int> companyIds, CancellationToken cancellationToken = default)
        {
            var list = new List<InsightsAgentConfig>();
            if (!Enabled || string.IsNullOrWhiteSpace(eventType))
                return list;
            var cids = (companyIds ?? new List<int>()).Where(c => c > 0).Distinct().ToArray();
            await EnsureSchemaAsync(cancellationToken);
            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            // Global agents (company_id null) match any event; company-scoped agents match if their company is
            // among those the event fans out to. Each agent row is returned once (no dup for a global agent).
            await using var cmd = new NpgsqlCommand(
                $"SELECT {Cols} FROM insights_agent WHERE tenant = @tenant AND enabled AND trigger_kind = 'event' " +
                "AND event_type = @etype AND (company_id IS NULL OR company_id = ANY(@cids))", conn);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            cmd.Parameters.AddWithValue("etype", eventType);
            cmd.Parameters.AddWithValue("cids", cids);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                list.Add(Map(reader));
            return list;
        }

        public async Task<InsightsAgentConfig> UpsertAsync(InsightsAgentConfig c, CancellationToken cancellationToken = default)
        {
            if (!Enabled || c == null)
                return null;
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

            await using (var conn = new NpgsqlConnection(_config.BuildConnectionString()))
            {
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO insights_agent (id, tenant, name, enabled, built_in, owner_user_id, company_id, trigger_kind, " +
                    "event_type, cron, filter, system_prompt, instruction, output_sinks, output_target, created_at, updated_at) " +
                    "VALUES (@id, @tenant, @name, @enabled, @builtin, @owner, @cid, @kind, @etype, @cron, @filter::jsonb, " +
                    "@sys, @instr, @sinks::jsonb, @target, @created, @updated) " +
                    "ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name, enabled=EXCLUDED.enabled, company_id=EXCLUDED.company_id, " +
                    "trigger_kind=EXCLUDED.trigger_kind, event_type=EXCLUDED.event_type, cron=EXCLUDED.cron, filter=EXCLUDED.filter, " +
                    "system_prompt=EXCLUDED.system_prompt, instruction=EXCLUDED.instruction, output_sinks=EXCLUDED.output_sinks, " +
                    "output_target=EXCLUDED.output_target, updated_at=EXCLUDED.updated_at", conn);
                cmd.Parameters.AddWithValue("id", c.Id);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("name", c.Name ?? "Agent");
                cmd.Parameters.AddWithValue("enabled", c.Enabled);
                cmd.Parameters.AddWithValue("builtin", c.BuiltIn);
                cmd.Parameters.AddWithValue("owner", (object)c.OwnerUserId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("cid", (object)c.CompanyId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("kind", c.TriggerKind ?? "event");
                cmd.Parameters.AddWithValue("etype", (object)c.EventType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("cron", (object)c.Cron ?? DBNull.Value);
                cmd.Parameters.AddWithValue("filter", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(c.FilterJson) ? "{}" : c.FilterJson);
                cmd.Parameters.AddWithValue("sys", (object)c.SystemPrompt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("instr", (object)c.Instruction ?? DBNull.Value);
                cmd.Parameters.AddWithValue("sinks", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(c.OutputSinksJson) ? "[]" : c.OutputSinksJson);
                cmd.Parameters.AddWithValue("target", (object)c.OutputTarget ?? DBNull.Value);
                cmd.Parameters.AddWithValue("created", DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc));
                cmd.Parameters.AddWithValue("updated", DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            SyncScheduleJob(c);
            return c;
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(id))
                return;
            await EnsureSchemaAsync(cancellationToken);
            await using (var conn = new NpgsqlConnection(_config.BuildConnectionString()))
            {
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand("DELETE FROM insights_agent WHERE tenant = @tenant AND id = @id", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            try { _recurringJobManager.RemoveIfExists(JobId(id)); } catch { /* ignore */ }
        }

        public async Task SyncSchedulesAsync(CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return;
            foreach (var c in await ListAsync(cancellationToken))
                SyncScheduleJob(c);
        }

        // Built-in agents shipped ready-to-enable (disabled by default so they don't run until the
        // user turns them on). Fixed ids → seeded once, never overwriting user edits (ON CONFLICT DO NOTHING).
        private static readonly InsightsAgentConfig[] BuiltIns = new[]
        {
            new InsightsAgentConfig
            {
                Id = "builtin-product-quality", Name = "Product-quality reviewer", BuiltIn = true, Enabled = false,
                TriggerKind = "event", EventType = "product-created",
                SystemPrompt = "You are a catalog quality assistant for MySnacks. You are READ-ONLY.",
                Instruction = "Check this new product against catalog guidelines: a clear descriptive name, an assigned vendor, an intended published state, and a sensible category. Flag anything missing or non-compliant in one short note.",
                OutputSinksJson = "[\"dashboard\"]"
            },
            new InsightsAgentConfig
            {
                Id = "builtin-vendor-analyzer", Name = "Vendor analyzer", BuiltIn = true, Enabled = false,
                TriggerKind = "schedule", Cron = "0 6 * * 1",
                SystemPrompt = "You are a vendor performance analyst for MySnacks. You are READ-ONLY.",
                Instruction = "From recent reviews and ordering trends, recommend products to decommission (stale or poorly rated) and best-sellers to promote. Keep it to a short, actionable list.",
                OutputSinksJson = "[\"dashboard\"]"
            },
            new InsightsAgentConfig
            {
                Id = "builtin-bad-review-responder", Name = "Bad-review responder", BuiltIn = true, Enabled = false,
                TriggerKind = "event", EventType = "review-added", FilterJson = "{\"maxRating\":2}",
                SystemPrompt = "You are a customer-care assistant for MySnacks. You are READ-ONLY.",
                Instruction = "Summarize this low-rated review's complaint and suggest a concrete resolution the vendor can act on. Be brief and constructive.",
                OutputSinksJson = "[\"dashboard\"]"
            }
        };

        public async Task SeedBuiltInsAsync(CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                foreach (var b in BuiltIns)
                {
                    await using var cmd = new NpgsqlCommand(
                        "INSERT INTO insights_agent (id, tenant, name, enabled, built_in, trigger_kind, event_type, cron, filter, " +
                        "system_prompt, instruction, output_sinks) VALUES (@id, @tenant, @name, false, true, @kind, @etype, @cron, " +
                        "@filter::jsonb, @sys, @instr, @sinks::jsonb) ON CONFLICT (id) DO NOTHING", conn);
                    cmd.Parameters.AddWithValue("id", b.Id);
                    cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                    cmd.Parameters.AddWithValue("name", b.Name);
                    cmd.Parameters.AddWithValue("kind", b.TriggerKind);
                    cmd.Parameters.AddWithValue("etype", (object)b.EventType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cron", (object)b.Cron ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("filter", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(b.FilterJson) ? "{}" : b.FilterJson);
                    cmd.Parameters.AddWithValue("sys", b.SystemPrompt);
                    cmd.Parameters.AddWithValue("instr", b.Instruction);
                    cmd.Parameters.AddWithValue("sinks", NpgsqlDbType.Text, b.OutputSinksJson ?? "[\"dashboard\"]");
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights agent: seed built-ins failed", ex);
            }
        }

        private void SyncScheduleJob(InsightsAgentConfig c)
        {
            var jobId = JobId(c.Id);
            try
            {
                if (c.Enabled && string.Equals(c.TriggerKind, "schedule", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Cron))
                    _recurringJobManager.AddOrUpdate<IInsightsAgentRunner>(
                        jobId, r => r.RunScheduledAsync(c.Id), c.Cron, new RecurringJobOptions { TimeZone = Tz });
                else
                    _recurringJobManager.RemoveIfExists(jobId);
            }
            catch (Exception ex)
            {
                _logger.WarningAsync($"Insights agent: schedule sync failed for {jobId}", ex).GetAwaiter().GetResult();
            }
        }

        private string JobId(string id) => $"insights-agent-{_config.Tenant}-{id}";

        private static InsightsAgentConfig Map(NpgsqlDataReader r) => new InsightsAgentConfig
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            Enabled = r.GetBoolean(2),
            BuiltIn = r.GetBoolean(3),
            OwnerUserId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
            CompanyId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
            TriggerKind = r.GetString(6),
            EventType = r.IsDBNull(7) ? null : r.GetString(7),
            Cron = r.IsDBNull(8) ? null : r.GetString(8),
            FilterJson = r.IsDBNull(9) ? null : r.GetString(9),
            SystemPrompt = r.IsDBNull(10) ? null : r.GetString(10),
            Instruction = r.IsDBNull(11) ? null : r.GetString(11),
            OutputSinksJson = r.IsDBNull(12) ? null : r.GetString(12),
            OutputTarget = r.IsDBNull(13) ? null : r.GetString(13),
            CreatedAt = r.GetDateTime(14),
            UpdatedAt = r.GetDateTime(15)
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
                    "CREATE TABLE IF NOT EXISTS insights_agent (" +
                    "  id text PRIMARY KEY, tenant text NOT NULL, name text NOT NULL, enabled boolean NOT NULL DEFAULT true," +
                    "  built_in boolean NOT NULL DEFAULT false, owner_user_id integer, company_id integer," +
                    "  trigger_kind text NOT NULL, event_type text, cron text, filter jsonb," +
                    "  system_prompt text, instruction text, output_sinks jsonb, output_target text," +
                    "  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now());" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_agent_match ON insights_agent (tenant, trigger_kind, event_type, enabled);";
                await using var cmd = new NpgsqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _schemaReady = true;
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        private static TimeZoneInfo ResolveTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
