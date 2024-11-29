using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using OllamaSharp;
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
            
            services.AddScoped<IOllamaApiClient>(_ => new OllamaApiClient("http://desktop:11434", 
                "llama3:instruct"));
        }
    }
}