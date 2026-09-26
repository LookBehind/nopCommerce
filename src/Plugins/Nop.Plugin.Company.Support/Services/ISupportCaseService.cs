using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Plugin.Company.Support.Domain;

namespace Nop.Plugin.Company.Support.Services
{
    public partial interface ISupportCaseService
    {
        Task<SupportCase> GetSupportCaseByIdAsync(int supportCaseId);

        /// <summary>
        /// The customer's own cases (mobile "my inquiries" list), newest first.
        /// </summary>
        Task<IList<SupportCase>> GetSupportCasesByCustomerIdAsync(int customerId, int storeId);

        /// <summary>
        /// Admin queue, filterable and paged.
        /// </summary>
        Task<IPagedList<SupportCase>> SearchSupportCasesAsync(
            int? statusId = null,
            int? categoryId = null,
            bool unassignedOnly = false,
            int pageIndex = 0,
            int pageSize = int.MaxValue);

        /// <summary>
        /// Inserts a new case and its initial Pending status-history row, in one call.
        /// </summary>
        Task<SupportCase> InsertSupportCaseAsync(SupportCase supportCase);

        Task UpdateSupportCaseAsync(SupportCase supportCase);

        /// <summary>
        /// Self-assigns a case to a staff customer. No-op (returns false) if already assigned
        /// to someone else - staff "take" a case, they don't reassign one another's here.
        /// </summary>
        Task<bool> AssignToCustomerAsync(int supportCaseId, int staffCustomerId);

        /// <summary>
        /// Changes status and appends a SupportCaseStatusHistory row. No-op if the case is
        /// already in that status.
        /// </summary>
        Task ChangeStatusAsync(int supportCaseId, SupportCaseStatus newStatus, int? changedByCustomerId);

        /// <summary>
        /// Full status timeline for one case, oldest first.
        /// </summary>
        Task<IList<SupportCaseStatusHistory>> GetStatusHistoryAsync(int supportCaseId);

        /// <summary>
        /// Full reply thread for one case, oldest first.
        /// </summary>
        Task<IList<SupportCaseMessage>> GetMessagesAsync(int supportCaseId);

        /// <summary>
        /// Appends a message to a case's reply thread. A staff reply (isStaff: true) pushes a
        /// notification to the case's own customer; a customer reply doesn't notify staff -
        /// they watch the admin queue instead.
        /// </summary>
        Task<SupportCaseMessage> AddMessageAsync(int supportCaseId, int authorCustomerId, bool isStaff, string body);
    }
}
