using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// Minimal Telegram Bot API client for scheduled report delivery (sendMessage, HTML).
    /// Self-contained (bot token from config) — does not depend on Notifications.Manager.
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
    }
}
