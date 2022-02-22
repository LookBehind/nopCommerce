using System;
using Nop.Core;
using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Services.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Nop.Plugin.Notifications.Manager
{
    public class NotificationManagerMethod : BasePlugin
    {
        private readonly IPluginService _pluginService;
        private readonly IScheduleTaskService _scheduleTaskService;

        public NotificationManagerMethod(IPluginService pluginService, IScheduleTaskService scheduleTaskService)
        {
            _pluginService = pluginService;
            _scheduleTaskService = scheduleTaskService;
        }

        public override async Task InstallAsync()
        {
            var pluginDescriptor =
                _pluginService.GetPluginDescriptorBySystemNameAsync<NotificationManagerMethod>("NotificationsManager");
            if (pluginDescriptor == null)
                throw new NopException("NotificationsManager is not installed!");

            if (await _scheduleTaskService.GetTaskByTypeAsync(TelegramNotificationSenderTask.TELEGRAM_NOTIFICATION_SENDER_TASK_NAME) == null)
            {
                await _scheduleTaskService.InsertTaskAsync(new Core.Domain.Tasks.ScheduleTask
                {
                    Enabled = true,
                    Name = TelegramNotificationSenderTask.TELEGRAM_NOTIFICATION_SENDER_FRIENDLY_NAME,
                    Type = TelegramNotificationSenderTask.TELEGRAM_NOTIFICATION_SENDER_TASK_NAME,
                    Seconds = 60
                });
            }

            var existingMailSendingTask = await
                _scheduleTaskService.GetTaskByTypeAsync("Nop.Services.Messages.QueuedMessagesSendTask, Nop.Services");
            if (existingMailSendingTask is { Enabled: true })
            {
                existingMailSendingTask.Enabled = false;
                await _scheduleTaskService.UpdateTaskAsync(existingMailSendingTask);
            }
        }
    }
}