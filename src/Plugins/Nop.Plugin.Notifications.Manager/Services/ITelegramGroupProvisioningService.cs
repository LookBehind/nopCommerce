using System.Threading.Tasks;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Auto-creates a Telegram group for a vendor (via an MTProto user-account client, since the Bot
/// API alone cannot create groups), adds the existing notification bot to it, and persists the
/// mapping through <see cref="IVendorTelegramChatCache"/> - the same GenericAttribute the manual
/// <c>/associate_with_vendor</c> flow writes.
/// </summary>
public interface ITelegramGroupProvisioningService
{
    /// <summary>
    /// Idempotent: does nothing if the vendor+store already has a chat mapping.
    /// </summary>
    Task ProvisionVendorGroupAsync(int vendorId, int storeId);
}
