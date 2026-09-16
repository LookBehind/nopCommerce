using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Nop.Services.Logging;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>Persists discovered Telegram chats (separate Postgres) so the target dropdown survives the
    /// ~24h getUpdates window. Best-effort — degrades to an empty list if Postgres/Telegram is unavailable.</summary>
    public class InsightsTelegramChatService : IInsightsTelegramChatService
    {
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaReady;

        private readonly InsightsMemoryConfig _config;
        private readonly InsightsTelegramClient _telegram;
        private readonly ILogger _logger;

        public InsightsTelegramChatService(InsightsMemoryConfig config, InsightsTelegramClient telegram, ILogger logger)
        {
            _config = config;
            _telegram = telegram;
            _logger = logger;
        }

        // Needs Postgres for the registry AND a Telegram bot to discover with.
        public bool Enabled => _config.Enabled && _telegram.Enabled;

        public async Task<IList<TelegramChat>> ListAsync(CancellationToken cancellationToken = default)
        {
            var list = new List<TelegramChat>();
            if (!_config.Enabled)
                return list;
            try
            {
                await EnsureSchemaAsync(cancellationToken);
                await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand(
                    "SELECT chat_id, title, type, username FROM insights_telegram_chat WHERE tenant = @tenant ORDER BY last_seen DESC", conn);
                cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    list.Add(new TelegramChat
                    {
                        Id = reader.GetString(0),
                        Title = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                        Type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Username = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights telegram chats: list failed", ex);
            }
            return list;
        }

        public async Task<IList<TelegramChat>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return await ListAsync(cancellationToken);
            try
            {
                var found = await _telegram.GetRecentChatsAsync(cancellationToken);
                if (found.Count > 0)
                {
                    await EnsureSchemaAsync(cancellationToken);
                    await using var conn = new NpgsqlConnection(_config.BuildConnectionString());
                    await conn.OpenAsync(cancellationToken);
                    foreach (var c in found)
                    {
                        await using var cmd = new NpgsqlCommand(
                            "INSERT INTO insights_telegram_chat (tenant, chat_id, title, type, username, last_seen) " +
                            "VALUES (@tenant, @id, @title, @type, @username, now()) " +
                            "ON CONFLICT (tenant, chat_id) DO UPDATE SET title = EXCLUDED.title, type = EXCLUDED.type, " +
                            "username = EXCLUDED.username, last_seen = now()", conn);
                        cmd.Parameters.AddWithValue("tenant", _config.Tenant);
                        cmd.Parameters.AddWithValue("id", c.Id);
                        cmd.Parameters.AddWithValue("title", (object)c.Title ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("type", (object)c.Type ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("username", (object)c.Username ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.WarningAsync("Insights telegram chats: discover failed", ex);
            }
            return await ListAsync(cancellationToken);
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
                await using var cmd = new NpgsqlCommand(
                    "CREATE TABLE IF NOT EXISTS insights_telegram_chat (" +
                    "  tenant text NOT NULL, chat_id text NOT NULL, title text, type text, username text," +
                    "  last_seen timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (tenant, chat_id));", conn);
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
