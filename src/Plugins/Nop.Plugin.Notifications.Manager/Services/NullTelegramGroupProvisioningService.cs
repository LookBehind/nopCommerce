using System;
using System.Threading.Tasks;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Registered when the Telegram user-account MTProto credentials aren't configured for this tenant.
/// Mirrors <see cref="Nop.Plugin.Notifications.Manager.Infrastructure.NullTelegramBotClient"/>: this
/// should never actually be invoked, because <see cref="EventConsumer.VendorTelegramGroupConsumer"/>
/// already guards on <see cref="NotificationManagerSettings.TelegramGroupAutoCreationEnabled"/> before
/// enqueueing any work - throwing here is a loud safety net, not the expected path.
/// </summary>
public class NullTelegramGroupProvisioningService : ITelegramGroupProvisioningService
{
    public Task ProvisionVendorGroupAsync(int vendorId, int storeId) =>
        throw new NotImplementedException(
            "Telegram group auto-creation was invoked but the MTProto user-account client isn't configured for this tenant.");
}
