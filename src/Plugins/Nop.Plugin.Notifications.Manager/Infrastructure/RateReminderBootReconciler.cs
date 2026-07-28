using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Services.Tasks;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

/// <summary>
/// Registers the initial set of rate-reminder Hangfire jobs once at boot, so a freshly
/// deployed/restarted pod doesn't sit with no jobs registered until someone next edits the
/// delivery-slot setting (which is when <see cref="EventConsumer.RateReminderSettingConsumer"/>
/// would otherwise be the only trigger). Invoked the same way as the existing
/// HangfireRecurringTaskRegistrar, via engine.ResolveAll&lt;IRecurringTaskRegistrar&gt;() at the
/// end of StartEngine (post-migration). See docs/plans/2026-07-29-rate-reminder-slot-jobs.md §4.4.
/// </summary>
public class RateReminderBootReconciler : IRecurringTaskRegistrar
{
    private readonly RateReminderReconciler _reconciler;

    public RateReminderBootReconciler(RateReminderReconciler reconciler)
    {
        _reconciler = reconciler;
    }

    public System.Threading.Tasks.Task RegisterAsync() => _reconciler.ReconcileAsync();
}
