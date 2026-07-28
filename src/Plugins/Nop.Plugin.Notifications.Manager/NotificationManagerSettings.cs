using Nop.Core.Configuration;

namespace Nop.Plugin.Notifications.Manager;

public class NotificationManagerSettings : ISettings
{
    /// <summary>
    /// Master switch for the vendor delivery Mini App feature (pre-delivery Telegram nudges +
    /// the "Open delivery board" link button attached to every vendor Telegram message). Off by
    /// default - turning it on also requires ExtendedAuthSettings.TelegramMiniAppSigningSecret to
    /// be configured for the tenant, or board-link minting has nothing to sign with.
    /// </summary>
    public bool VendorDeliveryMiniAppEnabled { get; set; }
}
