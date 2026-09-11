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

    /// <summary>
    /// JSON-encoded array of <see cref="Services.AutoInviteEntry"/> - Telegram users (identified by
    /// username or phone number, resolved and confirmed via the admin UI before being added) that get
    /// auto-added to every vendor Telegram group (existing ones when added to this list, and every
    /// future one at creation time), and swept out of every group when removed. Store-scoped like this
    /// plugin's other settings. Kept as a plain JSON string rather than a new DB table - this plugin
    /// has no custom tables today and the list is expected to stay small.
    /// </summary>
    public string AutoInviteTelegramUsersJson { get; set; }

    /// <summary>
    /// Bot token for the daily vendor weekly-totals Telegram report (<see cref="ScheduledTasks.VendorWeeklyTelegramReportTask"/>).
    /// Deliberately separate from ExtendedAuthSettings.TelegramBotSecret (the tenant's own vendor-facing
    /// bot) - the report posts into a shared internal ops group via its own dedicated bot, same as the
    /// external Redash-driven CronJob it replaces. Blank = report disabled for this tenant.
    /// </summary>
    public string TelegramReportBotToken { get; set; }

    /// <summary>
    /// Telegram chat id the weekly vendor report is posted to (e.g. "-805765254"). Blank = report
    /// disabled for this tenant. One report message is sent per <see cref="Nop.Core.Domain.Companies.Company"/>
    /// in the store, each captioned with that company's own name - there is no separate
    /// "company name" setting here.
    /// </summary>
    public string TelegramReportChatId { get; set; }
}
