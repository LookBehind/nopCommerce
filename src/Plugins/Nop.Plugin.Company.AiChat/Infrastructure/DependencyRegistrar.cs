using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Company.AiChat.Services;

namespace Nop.Plugin.Company.AiChat.Infrastructure
{
    public class DependencyRegistrar : IDependencyRegistrar
    {
        public virtual void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddScoped<IAiChatConversationService, AiChatConversationService>();
            services.AddScoped<IAiChatCatalogService, AiChatCatalogService>();
            services.AddScoped<IAiChatService, AiChatService>();

            // Chat completions go through the shared IKubeAiChatClient (Nop.Web.Framework's core
            // DependencyRegistrar) - same gateway/client Insights and Support use.
        }

        public int Order => 3;
    }
}
