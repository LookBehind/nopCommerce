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

        // Do NOT retry-storm a failing recurring task: fail once (visible on the dashboard) and let the next
        // CRON tick try again - matching the legacy timer's "log and move on" behavior. Without this, Hangfire's
        // default 10-attempt retry piles up Scheduled retries for any task that fails every run.
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public async System.Threading.Tasks.Task RunScheduleTaskAsync(string taskType)
        {
            if (string.IsNullOrWhiteSpace(taskType))
                return;

            // Scoped per taskType, not [DisableConcurrentExecution] - that attribute's resource key
            // is just DeclaringType.FullName + MethodName, it does NOT include the taskType argument,
            // so every distinct schedule task (RemindMe, RateReminder, PreDeliveryNudge, ...) was
            // serialized behind one shared lock. Right after a restart several recurring jobs come
            // due at once; whichever one runs long (RemindMe is documented to need up to 15 minutes
            // for LLM calls) held that single lock and made every OTHER task's run fail with
            // DistributedLockTimeoutException for as long as it ran - observed in prod as a ~46-minute
            // burst of failures across unrelated tasks right after a pod restart. This still prevents
            // a slow run from overlapping the next CRON tick for the SAME task, just not for others.
            using var taskLock = JobStorage.Current.GetConnection()
                .AcquireDistributedLock($"schedule-task:{taskType}", TimeSpan.FromSeconds(60));

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
