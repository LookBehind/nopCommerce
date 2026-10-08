using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Plugin.Company.Support.Domain;

namespace Nop.Plugin.Company.Support.Services
{
    /// <summary>
    /// Sends staff-facing email notifications about support cases.
    /// </summary>
    public interface ISupportCaseNotificationService
    {
        /// <summary>
        /// Notifies the store owner that a new support case was submitted.
        /// </summary>
        /// <param name="supportCase">The newly-created support case</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>The queued email identifiers (one per active, store-mapped template)</returns>
        Task<IList<int>> SendNewSupportCaseStoreOwnerNotificationAsync(SupportCase supportCase, int languageId);

        /// <summary>
        /// Notifies the store owner that the customer replied on their own support case.
        /// </summary>
        /// <param name="supportCase">The support case the customer replied on</param>
        /// <param name="messageBody">The customer's reply text</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>The queued email identifiers (one per active, store-mapped template)</returns>
        Task<IList<int>> SendSupportCaseCustomerRepliedStoreOwnerNotificationAsync(SupportCase supportCase, string messageBody, int languageId);
    }
}
