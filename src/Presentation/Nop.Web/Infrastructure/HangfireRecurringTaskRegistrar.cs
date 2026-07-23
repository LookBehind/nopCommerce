using System.Threading.Tasks;
using Hangfire;
using Nop.Services.Tasks;

namespace Nop.Web.Infrastructure
{
    /// <summary>
    /// Reconciles Hangfire recurring jobs against the ScheduleTask table: every enabled task that carries a
    /// CronExpression is (re)registered as a recurring job keyed by its Type; anything else is removed. Invoked
    /// from StartEngine after migrations have run (see IRecurringTaskRegistrar), so the CronExpression column is
    /// guaranteed to exist. The admin ScheduleTask controller performs the same reconcile on edit, so CRON
    /// changes take effect at runtime without an app restart.
    /// </summary>
    public partial class HangfireRecurringTaskRegistrar : IRecurringTaskRegistrar
    {
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly IScheduleTaskService _scheduleTaskService;

        public HangfireRecurringTaskRegistrar(IRecurringJobManager recurringJobManager,
            IScheduleTaskService scheduleTaskService)
        {
            _recurringJobManager = recurringJobManager;
            _scheduleTaskService = scheduleTaskService;
        }

        public async System.Threading.Tasks.Task RegisterAsync()
        {
            var tasks = await _scheduleTaskService.GetAllTasksAsync(true);

            foreach (var task in tasks)
            {
                //recurring-job id == task type (stable, one row = one type)
                var recurringJobId = task.Type;
                var taskType = task.Type;

                if (task.Enabled && !string.IsNullOrWhiteSpace(task.CronExpression))
                    _recurringJobManager.AddOrUpdate<HangfireScheduleTaskRunner>(recurringJobId,
                        runner => runner.RunScheduleTaskAsync(taskType), task.CronExpression);
                else
                    _recurringJobManager.RemoveIfExists(recurringJobId);
            }
        }
    }
}
