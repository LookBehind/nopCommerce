using System;
using System.Linq;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Nop.Services.Tasks;
using ILogger = Nop.Services.Logging.ILogger;

namespace Nop.Web.Infrastructure
{
    /// <summary>
    /// Executes a nopCommerce <see cref="IScheduleTask"/> in-process when triggered by a Hangfire recurring job.
    /// No HTTP self-POST (unlike the legacy <c>TaskThread</c>): Hangfire.Autofac opens a fresh Autofac lifetime
    /// scope per job, and the task instance (plus its dependencies) is resolved from THIS runner's injected
    /// <see cref="IServiceProvider"/> - i.e. that per-job scope - so scoped services (DB connection, etc.) are
    /// isolated per run. Timestamps mirror the legacy <c>Task.ExecuteTask</c> bookkeeping; failures propagate so
    /// Hangfire records/retries them and they show on the dashboard. See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    public partial class HangfireScheduleTaskRunner
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IScheduleTaskService _scheduleTaskService;
        private readonly ILogger _logger;

        public HangfireScheduleTaskRunner(IServiceProvider serviceProvider,
            IScheduleTaskService scheduleTaskService,
            ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _scheduleTaskService = scheduleTaskService;
            _logger = logger;
        }

        // Prevent a slow run from overlapping the next CRON tick for the same task (per task type + args).
        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        // Do NOT retry-storm a failing recurring task: fail once (visible on the dashboard) and let the next
        // CRON tick try again - matching the legacy timer's "log and move on" behavior. Without this, Hangfire's
        // default 10-attempt retry piles up Scheduled retries for any task that fails every run.
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public async System.Threading.Tasks.Task RunScheduleTaskAsync(string taskType)
        {
            if (string.IsNullOrWhiteSpace(taskType))
                return;

            var scheduleTask = await _scheduleTaskService.GetTaskByTypeAsync(taskType);
            if (scheduleTask == null || !scheduleTask.Enabled)
                return;

            //resolve the IScheduleTask CLR type (allow a bare type name, like the legacy runner)
            var type = Type.GetType(scheduleTask.Type) ??
                       AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => a.GetType(scheduleTask.Type))
                           .FirstOrDefault(t => t != null);
            if (type == null)
            {
                //unresolvable type (e.g. a task registered in the DB whose class was removed/never shipped).
                //Log and no-op rather than throwing, so Hangfire does not retry-storm this recurring job.
                await _logger.WarningAsync($"Schedule task type '{scheduleTask.Type}' could not be resolved; skipping run.");
                return;
            }

            //resolve the task within the per-job Autofac scope (so its scoped deps are isolated per run)
            var instance = (_serviceProvider.GetService(type)
                            ?? ActivatorUtilities.CreateInstance(_serviceProvider, type)) as IScheduleTask;
            if (instance == null)
                return;

            scheduleTask.LastStartUtc = DateTime.UtcNow;
            await _scheduleTaskService.UpdateTaskAsync(scheduleTask);

            try
            {
                await instance.ExecuteAsync();

                scheduleTask.LastEndUtc = scheduleTask.LastSuccessUtc = DateTime.UtcNow;
                await _scheduleTaskService.UpdateTaskAsync(scheduleTask);
            }
            catch (Exception exc)
            {
                scheduleTask.LastEndUtc = DateTime.UtcNow;
                //disable the task on error only if it is configured to stop on error (mirrors legacy behavior)
                scheduleTask.Enabled = !scheduleTask.StopOnError;
                await _scheduleTaskService.UpdateTaskAsync(scheduleTask);

                await _logger.ErrorAsync($"Error while running the '{scheduleTask.Name}' schedule task ({taskType})", exc);

                //rethrow so Hangfire records the failure (dashboard) and applies its retry policy
                throw;
            }
        }
    }
}
