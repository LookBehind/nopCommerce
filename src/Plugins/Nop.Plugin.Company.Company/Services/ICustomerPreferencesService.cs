using System.Threading.Tasks;
using Nop.Core.Domain.Customers;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Stores the mobile app's per-customer allergy/undesired-ingredient/avoided-vendor
    /// preferences, backing the v2 conflict-badge system (see
    /// docs/plans/2026-09-18-mobile-v2-implementation-plan.md §3.1).
    /// </summary>
    public partial interface ICustomerPreferencesService
    {
        /// <summary>
        /// Gets the current preferences for a customer. Never returns null - an unset
        /// preference comes back as an empty list, matching a customer who has never opened
        /// the wizard/editor.
        /// </summary>
        Task<CustomerPreferences> GetPreferencesAsync(Customer customer, int storeId);

        /// <summary>
        /// Upserts the preferences for a customer.
        /// </summary>
        Task SavePreferencesAsync(Customer customer, CustomerPreferences preferences, int storeId);
    }
}
