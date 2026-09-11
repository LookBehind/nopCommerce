using System;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Company.Insights.Services;

namespace Nop.Plugin.Company.Insights.Infrastructure
{
    /// <summary>
    /// Registers the plugin's services.
    /// </summary>
    public class DependencyRegistrar : IDependencyRegistrar
    {
        public virtual void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddScoped<IInsightsReportService, InsightsReportService>();
            services.AddScoped<IInsightsAgentService, InsightsAgentService>();
            services.AddScoped<IInsightsMemoryService, InsightsMemoryService>();
            services.AddScoped<IInsightsScheduleService, InsightsScheduleService>();
            services.AddScoped<IInsightsScheduleRunner, InsightsScheduleRunner>();
            services.AddSingleton<InsightsMemoryConfig>();

            // Typed client for the in-cluster KubeAI/vLLM gateway (OpenAI-compatible).
            services.AddHttpClient<InsightsLlmClient>(client =>
            {
                client.BaseAddress = new Uri(InsightsLlmClient.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(180);
            });

            // Typed client for the CPU embedder (absolute URL from config, so no BaseAddress).
            services.AddHttpClient<InsightsEmbedderClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // Typed client for the Telegram Bot API (absolute URL per call).
            services.AddHttpClient<InsightsTelegramClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        }

        public int Order => 3;
    }
}
