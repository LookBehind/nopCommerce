using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Vendors;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Vendors;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Nop.Plugin.Notifications.Manager.ScheduledTasks
{
    public class TelegramNotificationSenderTask : Services.Tasks.IScheduleTask
    {
        public const string TELEGRAM_NOTIFICATION_SENDER_TASK_NAME = "Nop.Plugin.Notifications.Manager.ScheduledTasks.TelegramNotificationSenderTask";
        public const string TELEGRAM_NOTIFICATION_SENDER_FRIENDLY_NAME = "Telegram notification sender";
        private const string VENDOR_TELEGRAM_CHANNEL_KEY = nameof(VENDOR_TELEGRAM_CHANNEL_KEY);
        private const string LAST_UPDATE_ID_SEEN_KEY = nameof(LAST_UPDATE_ID_SEEN_KEY);
        private static readonly string[] _trustedUsernames = {"lkbhnd", "hasmik_bars"};
        private static readonly TimeSpan _deleteEmailsOlderThan = TimeSpan.FromDays(7);
        
        private readonly IQueuedEmailService _queuedEmail;
        private readonly IVendorService _vendor;
        private readonly ITelegramBotClient _telegramBotClient;
        private readonly IGenericAttributeService _genericAttribute;
        private readonly ISettingService _setting;
        private readonly ILogger _logger;

        public TelegramNotificationSenderTask(IQueuedEmailService queuedEmail, 
            IVendorService vendor, 
            ITelegramBotClient telegramBotClient, 
            IGenericAttributeService genericAttribute, 
            ISettingService setting, 
            ILogger logger)
        {
            _queuedEmail = queuedEmail;
            _vendor = vendor;
            _telegramBotClient = telegramBotClient;
            _genericAttribute = genericAttribute;
            _setting = setting;
            _logger = logger;
        }

        private async Task UpdateVendorTelegramGroupsAsync()
        {
            try
            {
                var lastSeenUpdateId = await _setting.GetSettingByKeyAsync(LAST_UPDATE_ID_SEEN_KEY, 0);
                var updates = await _telegramBotClient.GetUpdatesAsync(
                    lastSeenUpdateId, timeout: 0,
                    allowedUpdates: new[] {UpdateType.Message});

                foreach (var update in updates)
                {
                    if (update.Type == UpdateType.Message && update.Message?.Type == MessageType.ChatMembersAdded &&
                        update.Message?.NewChatMembers?.Any(m => m.Id == _telegramBotClient.BotId) == true &&
                        _trustedUsernames.Contains(update.Message?.From?.Username))
                    {
                        try
                        {
                            var vendorNameFromMessage = Regex.Match(update.Message.Chat.Title,
                                "(.*?) orders.*",
                                RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant |
                                RegexOptions.IgnoreCase);

                            Vendor vendor;
                            if (!vendorNameFromMessage.Success ||
                                (vendor = (await _vendor.GetAllVendorsAsync(vendorNameFromMessage.Groups[1].Value)).SingleOrDefault()) == null)
                            {
                                await _telegramBotClient.SendTextMessageAsync(update.Message.Chat,
                                    "Couldn't match with vendor");
                                continue;
                            }
                        
                            await _genericAttribute.SaveAttributeAsync(vendor,
                                VENDOR_TELEGRAM_CHANNEL_KEY, update.Message.Chat.Id); 
                        }
                        catch (Exception e)
                        {
                            await _logger.ErrorAsync("Exception while handling telegram update, skipping", e);
                        }
                    }

                    lastSeenUpdateId = Math.Max(lastSeenUpdateId, update.Id);
                }
                
                await _setting.SetSettingAsync(LAST_UPDATE_ID_SEEN_KEY, lastSeenUpdateId);
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync("Exception while getting telegram updates, skipping", e);
            }
        }
        
        public async Task ExecuteAsync()
        {
            await UpdateVendorTelegramGroupsAsync();
            
            var maxTries = 3;
            
            var queuedEmails = await _queuedEmail.SearchEmailsAsync(null, null, 
                null, null,true, true, maxTries,
                false);
            foreach (var queuedEmail in queuedEmails)
            {
                var beingThrottled = false;
                try
                {
                    var (isVendorNotification, vendor) = await IsNotificationForVendorAsync(queuedEmail);
                    if (isVendorNotification)
                    {
                        var vendorGroupId = await _genericAttribute.GetAttributeAsync<long>(vendor,
                            VENDOR_TELEGRAM_CHANNEL_KEY, defaultValue: 0);

                        if (vendorGroupId != 0)
                        {
                            await _telegramBotClient.SendTextMessageAsync(vendorGroupId, queuedEmail.Body);
                        }
                    }

                    queuedEmail.SentOnUtc = DateTime.UtcNow;
                }
                catch (HttpRequestException exc) when (exc.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await _logger.ErrorAsync($"Telegram is throttling, ending task", exc);
                    beingThrottled = true;
                    return;
                }
                catch (Exception exc)
                {
                    await _logger.ErrorAsync($"Error sending telegram notification", exc);
                }
                finally
                {
                    if (!beingThrottled)
                    {
                        queuedEmail.SentTries += 1;
                        await _queuedEmail.UpdateQueuedEmailAsync(queuedEmail);
                    }
                }
            }

            await _queuedEmail.DeleteAlreadySentOrExpiredEmailsAsync(_deleteEmailsOlderThan);
        }

        private async Task<(bool, Vendor)> IsNotificationForVendorAsync(QueuedEmail queuedEmail)
        {
            var foundVendor = (await _vendor.GetAllVendorsAsync(email: queuedEmail.To)).SingleOrDefault();
            return foundVendor != null ? (true, foundVendor) : (false, null);
        }
    }
}