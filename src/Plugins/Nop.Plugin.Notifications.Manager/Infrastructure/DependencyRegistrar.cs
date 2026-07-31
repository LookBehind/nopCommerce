using System;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Notifications.Manager.Areas.Admin.Factories;
using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Tasks;
using Telegram.Bot;

namespace Nop.Plugin.Notifications.Manager.Infrastructure
{
    /// <summary>
    /// Dependency registrar
    /// </summary>
    public class DependencyRegistrar : IDependencyRegistrar
    {
        /// <summary>
        /// Gets order of this dependency registrar implementation
        /// </summary>
        public int Order
        {
            get { return 1100; }
        }

        /// <summary>
        /// Register services and interfaces
        /// </summary>
        /// <param name="builder">Container builder</param>
        /// <param name="typeFinder">Type finder</param>
        /// <param name="config">Config</param>
        public void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddSingleton<ITelegramBotClient>(_ =>
                appSettings.ExtendedAuthSettings.TelegramBotEnabled
                    ? new TelegramBotClient(appSettings.ExtendedAuthSettings.TelegramBotSecret)
                    : new NullTelegramBotClient());
            
            services.AddSingleton<FirebaseApp>(_ => FirebaseApp.Create(new AppOptions()
            {
                Credential = GoogleCredential.GetApplicationDefault(), 
                ProjectId = "mysnacks-d8778"
            }));
            
            services.AddScoped<PushNotificationService>();

            services.AddScoped<ITelegramMiniAppAuthService, TelegramMiniAppAuthService>();

            services.AddScoped<IVendorTelegramChatCache, VendorTelegramChatCache>();

            services.AddScoped<INotificationsManagerModelFactory, NotificationsManagerModelFactory>();

            var telegramUserAuthConfigured =
                appSettings.ExtendedAuthSettings.TelegramUserApiId != 0 &&
                !string.IsNullOrEmpty(appSettings.ExtendedAuthSettings.TelegramUserApiHash) &&
                !string.IsNullOrEmpty(appSettings.ExtendedAuthSettings.TelegramUserSessionPath);

            if (telegramUserAuthConfigured)
                services.AddScoped<ITelegramGroupProvisioningService, TelegramGroupProvisioningService>();
            else
                services.AddScoped<ITelegramGroupProvisioningService, NullTelegramGroupProvisioningService>();

            services.AddScoped<PreDeliveryNudgeReconciler>();
            services.AddScoped<PreDeliveryNudgeJob>();
            services.AddScoped<IRecurringTaskRegistrar, PreDeliveryNudgeBootReconciler>();

            services.AddScoped<RateReminderReconciler>();
            services.AddScoped<RateReminderJob>();
            services.AddScoped<IRecurringTaskRegistrar, RateReminderBootReconciler>();

            services.AddHttpClient<KubeAiChatClient>(client =>
                client.BaseAddress = new Uri(KubeAiChatClient.BaseUrl));
        }
    }
}