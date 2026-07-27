using Nop.Plugin.Notifications.Manager.ScheduledTasks;
using Nop.Services.Tasks;

namespace Nop.Plugin.Notifications.Manager.Infrastructure;

/// <summary>
/// Registers the initial set of pre-delivery-nudge Hangfire jobs once at boot, so a freshly
/// deployed/restarted pod doesn't sit with no jobs registered until someone next edits the
/// delivery-slot setting (which is when <see cref="EventConsumer.PreDeliveryNudgeSettingConsumer"/>
/// would otherwise be the only trigger). Invoked the same way as the existing
/// HangfireRecurringTaskRegistrar, via engine.ResolveAll&lt;IRecurringTaskRegistrar&gt;() at the
/// end of StartEngine (post-migration). See design §4.1.
/// </summary>
public class PreDeliveryNudgeBootReconciler : IRecurringTaskRegistrar
{
    private readonly PreDeliveryNudgeReconciler _reconciler;

    public PreDeliveryNudgeBootReconciler(PreDeliveryNudgeReconciler reconciler)
    {
        _reconciler = reconciler;
    }

    public System.Threading.Tasks.Task RegisterAsync() => _reconciler.ReconcileAsync();
}
