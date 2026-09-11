using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nop.Plugin.Company.Insights.Models;

namespace Nop.Plugin.Company.Insights.Services
{
    /// <summary>
    /// CRUD for scheduled reports (stored in the separate Postgres) that also registers/removes the
    /// matching Hangfire recurring job so schedules take effect without a restart.
    /// </summary>
    public interface IInsightsScheduleService
    {
        /// <summary>True when the backing Postgres is configured.</summary>
        bool Enabled { get; }

        Task<IList<InsightsSchedule>> ListAsync(CancellationToken cancellationToken = default);
        Task<InsightsSchedule> GetAsync(string id, CancellationToken cancellationToken = default);
        Task<InsightsSchedule> UpsertAsync(InsightsSchedule schedule, CancellationToken cancellationToken = default);
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    }
}
