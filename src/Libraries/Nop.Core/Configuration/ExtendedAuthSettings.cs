

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
    }
}
