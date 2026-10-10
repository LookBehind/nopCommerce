using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nop.Services.Catalog;
using Nop.Services.Events;
using Nop.Services.Logging;
using Nop.Services.Vendors;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Nop.Plugin.Notifications.Manager.EventConsumer;

/// <summary>
/// Posts product edits (who changed what, old -> new) to a dedicated Telegram chat configured on the
/// plugin's admin Configure page. Uses the same internal ops bot as the weekly vendor report
/// (<see cref="NotificationManagerSettings.TelegramReportBotToken"/>) but its own chat
/// (<see cref="NotificationManagerSettings.ProductChangeTelegramChatId"/>). Blank chat id = disabled.
///
/// Auto-discovered via the IConsumer&lt;T&gt; convention. Never throws: a Telegram hiccup must not fail
/// the product save that triggered it, and the call is capped by a short timeout.
/// </summary>
public class ProductChangeTelegramConsumer : IConsumer<ProductChangedEvent>
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly NotificationManagerSettings _settings;
    private readonly IVendorService _vendorService;
    private readonly ILogger _logger;

    public ProductChangeTelegramConsumer(NotificationManagerSettings settings, IVendorService vendorService, ILogger logger)
    {
        _settings = settings;
        _vendorService = vendorService;
        _logger = logger;
    }

    public async Task HandleEventAsync(ProductChangedEvent eventMessage)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductChangeTelegramChatId) ||
            string.IsNullOrWhiteSpace(_settings.TelegramReportBotToken))
            return;

        if (!long.TryParse(_settings.ProductChangeTelegramChatId, out var chatIdValue))
        {
            await _logger.WarningAsync($"Product change Telegram feed: ProductChangeTelegramChatId '{_settings.ProductChangeTelegramChatId}' is not a valid chat id - skipping.");
            return;
        }

        try
        {
            var product = eventMessage.Product;
            var vendor = product.VendorId > 0 ? await _vendorService.GetVendorByIdAsync(product.VendorId) : null;

            var text = new StringBuilder();
            text.AppendLine($"<b>Product edited</b> ({WebUtility.HtmlEncode(eventMessage.Source)})");
            text.AppendLine($"Product: {WebUtility.HtmlEncode(product.Name)} (#{product.Id})");
            if (vendor != null)
                text.AppendLine($"Vendor: {WebUtility.HtmlEncode(vendor.Name)}");
            text.AppendLine($"By: {WebUtility.HtmlEncode(string.IsNullOrEmpty(eventMessage.EditorEmail) ? "unknown" : eventMessage.EditorEmail)}");
            text.AppendLine();
            foreach (var change in eventMessage.Changes.Split("; ", StringSplitOptions.RemoveEmptyEntries))
                text.AppendLine($"• {WebUtility.HtmlEncode(change)}");

            using var cts = new CancellationTokenSource(SendTimeout);
            await new TelegramBotClient(_settings.TelegramReportBotToken).SendMessage(
                chatId: new ChatId(chatIdValue),
                text: text.ToString(),
                parseMode: ParseMode.Html,
                cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            await _logger.WarningAsync($"Product change Telegram feed: failed to post change for product {eventMessage.Product.Id}: {ex.Message}", ex);
        }
    }
}
