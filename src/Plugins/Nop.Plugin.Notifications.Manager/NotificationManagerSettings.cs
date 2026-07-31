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

    /// <summary>
    /// Master switch for auto-creating a Telegram group (via the MTProto user-account client) and
    /// adding the bot whenever a new Vendor is inserted. Off by default - also requires
    /// ExtendedAuthSettings.TelegramUserApiId/TelegramUserApiHash/TelegramUserSessionPath to be
    /// configured with a valid, already-authorized session for the tenant.
    /// </summary>
    public bool TelegramGroupAutoCreationEnabled { get; set; }
}
