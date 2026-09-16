using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>A chat the bot has seen (group/supergroup/channel) — used to pick a Telegram target by name.</summary>
    public class TelegramChat
    {
        public string Id { get; set; }        // chat id ("-100..." for supergroups/channels)
        public string Title { get; set; }     // group/channel title (or @username / id fallback)
        public string Type { get; set; }      // group | supergroup | channel
        public string Username { get; set; }  // optional public @username
    }

    /// <summary>
    /// Minimal Telegram Bot API client: sendMessage (HTML) + chat discovery. Self-contained (bot token
    /// from config) — does not depend on Notifications.Manager.
    /// </summary>
    public class InsightsTelegramClient
    {
        private readonly HttpClient _httpClient;
        private readonly InsightsMemoryConfig _config;

        public InsightsTelegramClient(HttpClient httpClient, InsightsMemoryConfig config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public bool Enabled => _config.TelegramEnabled;

        public async Task<bool> SendMessageAsync(string chatId, string html, CancellationToken cancellationToken = default)
        {
            if (!_config.TelegramEnabled || string.IsNullOrWhiteSpace(chatId))
                return false;

            var url = $"https://api.telegram.org/bot{_config.TelegramBotToken}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = html,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Telegram has no "list my groups" API, so we harvest the chats the bot has recently interacted
        /// with from getUpdates (a group appears when the bot is added — a my_chat_member update — or when
        /// someone runs a command / @mentions it there). Advances the offset to consume the updates so they
        /// don't pile up (this bot is send-only with no other poller). Best-effort; returns groups/channels only.
        /// </summary>
        public async Task<IList<TelegramChat>> GetRecentChatsAsync(CancellationToken cancellationToken = default)
        {
            var chats = new Dictionary<string, TelegramChat>();
            if (!_config.TelegramEnabled || string.IsNullOrWhiteSpace(_config.TelegramBotToken))
                return new List<TelegramChat>();

            var baseUrl = $"https://api.telegram.org/bot{_config.TelegramBotToken}";
            // allowed_updates = ["message","my_chat_member","channel_post","edited_message"] (URL-encoded)
            var url = $"{baseUrl}/getUpdates?timeout=0&limit=100&allowed_updates=%5B%22message%22%2C%22my_chat_member%22%2C%22channel_post%22%2C%22edited_message%22%5D";

            long? maxUpdateId = null;
            using (var resp = await _httpClient.GetAsync(url, cancellationToken))
            {
                if (!resp.IsSuccessStatusCode)
                    return new List<TelegramChat>();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
                if (!doc.RootElement.TryGetProperty("result", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return new List<TelegramChat>();

                foreach (var upd in arr.EnumerateArray())
                {
                    if (upd.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var id))
                        maxUpdateId = maxUpdateId.HasValue ? Math.Max(maxUpdateId.Value, id) : id;
                    var chat = ExtractChat(upd);
                    if (chat != null)
                        chats[chat.Id] = chat;
                }
            }

            // Confirm the batch so the same updates aren't returned forever (no other consumer of this bot).
            if (maxUpdateId.HasValue)
            {
                try
                {
                    using var _ = await _httpClient.GetAsync($"{baseUrl}/getUpdates?offset={maxUpdateId.Value + 1}&timeout=0", cancellationToken);
                }
                catch { /* best-effort */ }
            }

            return chats.Values
                .Where(c => c.Type == "group" || c.Type == "supergroup" || c.Type == "channel")
                .ToList();
        }

        private static TelegramChat ExtractChat(JsonElement upd)
        {
            foreach (var key in new[] { "message", "channel_post", "edited_message", "my_chat_member" })
            {
                if (!upd.TryGetProperty(key, out var container) || !container.TryGetProperty("chat", out var chat))
                    continue;
                if (!chat.TryGetProperty("id", out var cid))
                    continue;
                var id = cid.GetRawText(); // numeric literal, e.g. -1001234567890
                return new TelegramChat
                {
                    Id = id,
                    Type = chat.TryGetProperty("type", out var ty) ? ty.GetString() : "",
                    Username = chat.TryGetProperty("username", out var un) ? un.GetString() : null,
                    Title = chat.TryGetProperty("title", out var t) && !string.IsNullOrWhiteSpace(t.GetString())
                        ? t.GetString()
                        : (chat.TryGetProperty("username", out var un2) ? "@" + un2.GetString() : id)
                };
            }
            return null;
        }
    }
}
