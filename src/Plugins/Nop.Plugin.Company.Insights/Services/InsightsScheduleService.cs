using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Npgsql;
using Nop.Plugin.Company.Insights.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Postgres-backed scheduled-report store + Hangfire recurring-job sync. Times run in
    /// Asia/Yerevan (bare crons would otherwise use the container's UTC/Berlin default and drift).
    /// </summary>
    public class InsightsScheduleService : IInsightsScheduleService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;
        private static readonly TimeZoneInfo Tz = ResolveTz();

        private readonly InsightsMemoryConfig _config;
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly ILogger _logger;

        public InsightsScheduleService(
            InsightsMemoryConfig config,
            IRecurringJobManager recurringJobManager,
            ILogger logger)
        {
            _config = config;
            _recurringJobManager = recurringJobManager;
            _logger = logger;
        }

        public bool Enabled => _config.Enabled;

        public async Task<IList<InsightsSchedule>> ListAsync(CancellationToken cancellationToken = default)
        {
            var list = new List<InsightsSchedule>();
            if (!Enabled)
                return list;

            await EnsureSchemaAsync(cancellationToken);
            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, report_id, cron, telegram_chat_id, enabled, created_at " +
                "FROM insights_schedule WHERE tenant = @tenant ORDER BY created_at", conn);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                list.Add(Map(reader));
            return list;
        }

        public async Task<InsightsSchedule> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(id))
                return null;

            await EnsureSchemaAsync(cancellationToken);
            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, report_id, cron, telegram_chat_id, enabled, created_at " +
                "FROM insights_schedule WHERE tenant = @tenant AND id = @id", conn);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
        }

        public async Task<InsightsSchedule> UpsertAsync(InsightsSchedule schedule, CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                throw new InvalidOperationException("Scheduling is not configured (no Postgres).");

            await EnsureSchemaAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(schedule.Id))
            {
                schedule.Id = Guid.NewGuid().ToString("N");
                schedule.CreatedAt = DateTime.UtcNow;
            }
            else if (schedule.CreatedAt == default)
            {
                schedule.CreatedAt = DateTime.UtcNow;
            }

            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO insights_schedule (id, tenant, name, report_id, cron, telegram_chat_id, enabled, created_at) " +
                "VALUES (@id, @tenant, @name, @report, @cron, @chat, @enabled, @created) " +
                "ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, report_id = EXCLUDED.report_id, " +
                "cron = EXCLUDED.cron, telegram_chat_id = EXCLUDED.telegram_chat_id, enabled = EXCLUDED.enabled", conn);
            cmd.Parameters.AddWithValue("id", schedule.Id);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            cmd.Parameters.AddWithValue("name", schedule.Name ?? "Report");
            cmd.Parameters.AddWithValue("report", schedule.ReportId ?? "");
            cmd.Parameters.AddWithValue("cron", schedule.Cron ?? "");
            cmd.Parameters.AddWithValue("chat", schedule.TelegramChatId ?? "");
            cmd.Parameters.AddWithValue("enabled", schedule.Enabled);
            cmd.Parameters.AddWithValue("created", DateTime.SpecifyKind(schedule.CreatedAt, DateTimeKind.Utc));
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            SyncHangfire(schedule);
            return schedule;
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(id))
                return;

            await EnsureSchemaAsync(cancellationToken);
            await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM insights_schedule WHERE tenant = @tenant AND id = @id", conn);
            cmd.Parameters.AddWithValue("tenant", _config.Tenant);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _recurringJobManager.RemoveIfExists(JobId(id));
        }

        private void SyncHangfire(InsightsSchedule s)
        {
            var jobId = JobId(s.Id);
            try
            {
                if (s.Enabled && !string.IsNullOrWhiteSpace(s.Cron))
                    _recurringJobManager.AddOrUpdate<IInsightsScheduleRunner>(
                        jobId, runner => runner.RunAsync(s.Id), s.Cron,
                        new RecurringJobOptions { TimeZone = Tz });
                else
                    _recurringJobManager.RemoveIfExists(jobId);
            }
            catch (Exception ex)
            {
                _logger.WarningAsync($"Insights: failed to register recurring job {jobId}", ex).GetAwaiter().GetResult();
            }
        }

        private string JobId(string id) => $"insights-schedule-{_config.Tenant}-{id}";

        private static InsightsSchedule Map(NpgsqlDataReader r) => new InsightsSchedule
        {
            Id = r.GetString(0),
            Name = r.GetString(1),
            ReportId = r.GetString(2),
            Cron = r.GetString(3),
            TelegramChatId = r.GetString(4),
            Enabled = r.GetBoolean(5),
            CreatedAt = r.GetDateTime(6)
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
                await using var cmd = new NpgsqlCommand(
                    "CREATE TABLE IF NOT EXISTS insights_schedule (" +
                    "  id text PRIMARY KEY," +
                    "  tenant text NOT NULL," +
                    "  name text NOT NULL," +
                    "  report_id text NOT NULL," +
                    "  cron text NOT NULL," +
                    "  telegram_chat_id text NOT NULL," +
                    "  enabled boolean NOT NULL DEFAULT true," +
                    "  created_at timestamptz NOT NULL DEFAULT now());" +
                    "CREATE INDEX IF NOT EXISTS idx_insights_schedule_tenant ON insights_schedule (tenant);", conn);
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
