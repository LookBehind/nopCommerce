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
            services.AddScoped<IInsightsProfileService, InsightsProfileService>();
            services.AddScoped<IInsightsMemoryService, InsightsMemoryService>();
            services.AddScoped<IInsightsWorkspaceService, InsightsWorkspaceService>();
            services.AddScoped<IInsightsScheduleService, InsightsScheduleService>();
            services.AddScoped<IInsightsScheduleRunner, InsightsScheduleRunner>();
            services.AddScoped<IInsightsEventService, InsightsEventService>();
            services.AddScoped<IInsightsEventDispatcher, InsightsEventDispatcher>();
            services.AddScoped<IInsightsCompanyResolver, InsightsCompanyResolver>();
            services.AddScoped<InsightsDeliveryTriggerReconciler>();
            services.AddScoped<IInsightsDeliveryTriggerJob, InsightsDeliveryTriggerJob>();
            services.AddScoped<Nop.Services.Tasks.IRecurringTaskRegistrar, InsightsDeliveryTriggerBootReconciler>();
            services.AddScoped<IInsightsAgentConfigService, InsightsAgentConfigService>();
            services.AddScoped<IInsightsAgentRunService, InsightsAgentRunService>();
            services.AddScoped<IInsightsAgentRunner, InsightsAgentRunner>();
            services.AddScoped<IInsightsRetentionJob, InsightsRetentionJob>();
            services.AddScoped<Nop.Services.Tasks.IRecurringTaskRegistrar, InsightsAgentBootRegistrar>();
            services.AddScoped<IInsightsTelegramChatService, InsightsTelegramChatService>();
            services.AddSingleton<InsightsMemoryConfig>();

            // Chat completions go through the shared IKubeAiChatClient (Nop.Web.Framework's core
            // DependencyRegistrar) - its HttpClient.Timeout is sized for this agent's long tool-calling
            // turns, the longest of any consumer.

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
