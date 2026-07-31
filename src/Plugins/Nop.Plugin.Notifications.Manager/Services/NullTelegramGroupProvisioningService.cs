using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Registered when the Telegram user-account MTProto credentials aren't configured for this tenant.
/// Mirrors <see cref="Nop.Plugin.Notifications.Manager.Infrastructure.NullTelegramBotClient"/>: this
/// should never actually be invoked, because <see cref="EventConsumer.VendorTelegramGroupConsumer"/>
/// already guards on <see cref="NotificationManagerSettings.TelegramGroupAutoCreationEnabled"/> before
/// enqueueing any work, and the admin config page should surface this precondition clearly rather
/// than let the exception surface raw - throwing here is a loud safety net, not the expected path.
/// </summary>
public class NullTelegramGroupProvisioningService : ITelegramGroupProvisioningService
{
    private const string ERROR_MESSAGE =
        "Telegram group auto-creation was invoked but the MTProto user-account client isn't configured for this tenant.";

    public Task ProvisionVendorGroupAsync(int vendorId, int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<IReadOnlyList<string>> GetAutoInviteUsernamesAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task AddAutoInviteUserAsync(int storeId, string username) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task RemoveAutoInviteUserAsync(int storeId, string username) =>
        throw new NotImplementedException(ERROR_MESSAGE);

    public Task<int> GetGroupCountAsync(int storeId) =>
        throw new NotImplementedException(ERROR_MESSAGE);
}
