using Nop.Plugin.Company.Insights.Services;
using Nop.Services.Tasks;

namespace Nop.Plugin.Company.Insights.Infrastructure
{
    /// <summary>
    /// Registers the delivery-approaching / day-closing Hangfire jobs once at boot (resolved via
    /// engine.ResolveAll&lt;IRecurringTaskRegistrar&gt;() at the end of StartEngine), so a freshly
    /// restarted pod has its time-trigger jobs without waiting for a slot-setting change.
    /// </summary>
    public class InsightsDeliveryTriggerBootReconciler : IRecurringTaskRegistrar
    {
        private readonly InsightsDeliveryTriggerReconciler _reconciler;

        public InsightsDeliveryTriggerBootReconciler(InsightsDeliveryTriggerReconciler reconciler)
        {
            _reconciler = reconciler;
        }

        // Fully-qualify: Nop.Services.Tasks also defines a `Task` type.
        public System.Threading.Tasks.Task RegisterAsync() => _reconciler.ReconcileAsync();
    }
}
