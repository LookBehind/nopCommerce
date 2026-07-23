using Autofac.Extensions.DependencyInjection;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Tasks;

namespace Nop.Web.Infrastructure
{
    /// <summary>
    /// Configures Hangfire as the dynamic / cron-capable scheduling engine. It coexists with the legacy
    /// timer-based ScheduleTask engine (TaskManager): tasks that carry a CronExpression are owned by
    /// Hangfire, the rest keep running on the legacy interval timer.
    /// Job storage is the tenant's OWN database, chosen at runtime from dataSettings.json, so every tenant
    /// container gets an isolated Hangfire schema. See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    public partial class HangfireStartup : INopStartup
    {
        /// <summary>
        /// Whether the current data provider has a supported Hangfire storage.
        /// </summary>
        private static bool IsSupportedProvider(DataSettings dataSettings)
        {
            return dataSettings?.DataProvider is DataProviderType.SqlServer or DataProviderType.PostgreSQL;
        }

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            //nothing to store jobs in until the app is installed
            if (!DataSettingsManager.IsDatabaseInstalled())
                return;

            var dataSettings = DataSettingsManager.LoadSettings();
            if (!IsSupportedProvider(dataSettings))
                return;

            services.AddHangfire(config =>
            {
                config
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings();

                switch (dataSettings.DataProvider)
                {
                    case DataProviderType.SqlServer:
                        config.UseSqlServerStorage(dataSettings.ConnectionString);
                        break;
                    case DataProviderType.PostgreSQL:
                        config.UsePostgreSqlStorage(dataSettings.ConnectionString);
                        break;
                }
            });

            //the background server (IHostedService) that actually processes recurring/enqueued jobs
            services.AddHangfireServer();

            //in-process executor for CRON-triggered schedule tasks (resolved per-job by Hangfire.Autofac)
            services.AddScoped<HangfireScheduleTaskRunner>();

            //registers CRON-driven schedule tasks as recurring jobs; invoked from StartEngine after migrations
            services.AddScoped<IRecurringTaskRegistrar, HangfireRecurringTaskRegistrar>();
        }

        public void Configure(IApplicationBuilder application)
        {
            if (!DataSettingsManager.IsDatabaseInstalled())
                return;

            if (!IsSupportedProvider(DataSettingsManager.LoadSettings()))
                return;

            //resolve jobs (and their scoped nop dependencies) through the Autofac container;
            //the activator opens a child lifetime scope per job execution
            GlobalConfiguration.Configuration.UseAutofacActivator(application.ApplicationServices.GetAutofacRoot());

            //dashboard, gated by the same permission as the admin Schedule Tasks page
            application.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = new[] { new HangfireDashboardAuthorizationFilter() },
                DisplayStorageConnectionString = false
            });

            //NOTE: recurring jobs are NOT registered here - Configure() runs before nopCommerce applies its
            //FluentMigrator migrations (StartEngine), so the CronExpression column may not exist yet. Registration
            //happens via HangfireRecurringTaskRegistrar (IRecurringTaskRegistrar), invoked from StartEngine after
            //migrations.
        }

        /// <summary>
        /// After Authorization middleware (600) and before the MVC endpoints (1000).
        /// </summary>
        public int Order => 700;
    }
}
