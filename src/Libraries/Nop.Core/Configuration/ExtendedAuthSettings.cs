

namespace Nop.Core.Configuration
{
    public class ExtendedAuthSettings
    {
        public string TelegramBotSecret { get; set; }
        public bool TelegramBotEnabled { get; set; }

        /// <summary>
        /// Local secret used to sign vendor-delivery-board tokens embedded in Telegram Mini App
        /// links. Unrelated to the bot token itself.
        /// </summary>
        public string TelegramMiniAppSigningSecret { get; set; }

        /// <summary>
        /// api_id from my.telegram.org, used by the MTProto user-account client that creates
        /// vendor Telegram groups (the Bot API cannot create groups on its own).
        /// </summary>
        public int TelegramUserApiId { get; set; }

        /// <summary>
        /// api_hash from my.telegram.org, paired with <see cref="TelegramUserApiId"/>.
        /// </summary>
        public string TelegramUserApiHash { get; set; }

        /// <summary>
        /// Path to the persisted MTProto session file for the user account used to create vendor
        /// groups. Produced by a one-time interactive login done out-of-band; the running app only
        /// ever reads an already-authorized session from this path.
        /// </summary>
        public string TelegramUserSessionPath { get; set; }
    }
}
