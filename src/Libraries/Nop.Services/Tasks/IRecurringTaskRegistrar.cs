namespace Nop.Services.Tasks
{
    /// <summary>
    /// Registers dynamic (CRON) schedule tasks with an external recurring-job scheduler (e.g. Hangfire).
    /// Implementations are invoked once during application startup, AFTER database migrations have been applied,
    /// so they may safely read schedule-task columns added by migrations (e.g. CronExpression).
    /// See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    public interface IRecurringTaskRegistrar
    {
        /// <summary>
        /// Reconcile external recurring jobs against the current schedule-task configuration.
        /// </summary>
        System.Threading.Tasks.Task RegisterAsync();
    }
}
