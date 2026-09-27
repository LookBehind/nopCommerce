using System;
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

            // Typed client for the in-cluster KubeAI/vLLM gateway (OpenAI-compatible) - same
            // gateway/model as Nop.Plugin.Company.Insights, registered independently since
            // plugins in this codebase don't take project references on each other.
            services.AddHttpClient<AiChatLlmClient>(client =>
            {
                client.BaseAddress = new Uri(AiChatLlmClient.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(180);
            });
        }

        public int Order => 3;
    }
}
