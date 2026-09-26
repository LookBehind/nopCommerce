using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Data;
using Nop.Plugin.Company.Support.Domain;
using Nop.Services.Notifications;

namespace Nop.Plugin.Company.Support.Services
{
    public partial class SupportCaseService : ISupportCaseService
    {
        private readonly IRepository<SupportCase> _supportCaseRepository;
        private readonly IRepository<SupportCaseStatusHistory> _statusHistoryRepository;
        private readonly IRepository<SupportCaseMessage> _messageRepository;
        private readonly IPushNotificationService _pushNotificationService;

        public SupportCaseService(
            IRepository<SupportCase> supportCaseRepository,
            IRepository<SupportCaseStatusHistory> statusHistoryRepository,
            IRepository<SupportCaseMessage> messageRepository,
            IPushNotificationService pushNotificationService)
        {
            _supportCaseRepository = supportCaseRepository;
            _statusHistoryRepository = statusHistoryRepository;
            _messageRepository = messageRepository;
            _pushNotificationService = pushNotificationService;
        }

        public virtual async Task<SupportCase> GetSupportCaseByIdAsync(int supportCaseId)
        {
            return await _supportCaseRepository.GetByIdAsync(supportCaseId, cache => default);
        }

        public virtual async Task<IList<SupportCase>> GetSupportCasesByCustomerIdAsync(int customerId, int storeId)
        {
            return await _supportCaseRepository.GetAllAsync(query =>
            {
                return query
                    .Where(c => c.CustomerId == customerId && c.StoreId == storeId)
                    .OrderByDescending(c => c.CreatedOnUtc);
            });
        }

        public virtual async Task<IPagedList<SupportCase>> SearchSupportCasesAsync(
            int? statusId = null,
            int? categoryId = null,
            bool unassignedOnly = false,
            int pageIndex = 0,
            int pageSize = int.MaxValue)
        {
            return await _supportCaseRepository.GetAllPagedAsync(query =>
            {
                if (statusId.HasValue)
                    query = query.Where(c => c.StatusId == statusId.Value);
                if (categoryId.HasValue)
                    query = query.Where(c => c.CategoryId == categoryId.Value);
                if (unassignedOnly)
                    query = query.Where(c => c.AssignedToCustomerId == null);

                return query.OrderByDescending(c => c.CreatedOnUtc);
            }, pageIndex, pageSize);
        }

        public virtual async Task<SupportCase> InsertSupportCaseAsync(SupportCase supportCase)
        {
            supportCase.Status = SupportCaseStatus.Pending;
            supportCase.CreatedOnUtc = DateTime.UtcNow;
            supportCase.UpdatedOnUtc = supportCase.CreatedOnUtc;

            await _supportCaseRepository.InsertAsync(supportCase);

            await _statusHistoryRepository.InsertAsync(new SupportCaseStatusHistory
            {
                SupportCaseId = supportCase.Id,
                Status = SupportCaseStatus.Pending,
                EnteredOnUtc = supportCase.CreatedOnUtc,
                ChangedByCustomerId = null
            });

            return supportCase;
        }

        public virtual async Task UpdateSupportCaseAsync(SupportCase supportCase)
        {
            await _supportCaseRepository.UpdateAsync(supportCase);
        }

        public virtual async Task<bool> AssignToCustomerAsync(int supportCaseId, int staffCustomerId)
        {
            var supportCase = await GetSupportCaseByIdAsync(supportCaseId);
            if (supportCase == null || supportCase.AssignedToCustomerId.HasValue)
                return false;

            supportCase.AssignedToCustomerId = staffCustomerId;
            await UpdateSupportCaseAsync(supportCase);
            return true;
        }

        public virtual async Task ChangeStatusAsync(int supportCaseId, SupportCaseStatus newStatus, int? changedByCustomerId)
        {
            var supportCase = await GetSupportCaseByIdAsync(supportCaseId);
            if (supportCase == null || supportCase.Status == newStatus)
                return;

            supportCase.Status = newStatus;
            supportCase.UpdatedOnUtc = DateTime.UtcNow;
            await UpdateSupportCaseAsync(supportCase);

            await _statusHistoryRepository.InsertAsync(new SupportCaseStatusHistory
            {
                SupportCaseId = supportCaseId,
                Status = newStatus,
                EnteredOnUtc = supportCase.UpdatedOnUtc,
                ChangedByCustomerId = changedByCustomerId
            });

            // Only staff (via the admin Edit page) ever call this today, so the case's own
            // customer is always someone other than changedByCustomerId - safe to always notify.
            var statusText = SupportCaseDisplayNames.Status.TryGetValue(newStatus, out var text) ? text : newStatus.ToString();
            await NotifyCustomerAsync(
                supportCase,
                "Support case update",
                $"Your support case \"{supportCase.Subject}\" is now {statusText}.");
        }

        public virtual async Task<IList<SupportCaseStatusHistory>> GetStatusHistoryAsync(int supportCaseId)
        {
            return await _statusHistoryRepository.GetAllAsync(query =>
            {
                return query
                    .Where(h => h.SupportCaseId == supportCaseId)
                    .OrderBy(h => h.EnteredOnUtc);
            });
        }

        public virtual async Task<IList<SupportCaseMessage>> GetMessagesAsync(int supportCaseId)
        {
            return await _messageRepository.GetAllAsync(query =>
            {
                return query
                    .Where(m => m.SupportCaseId == supportCaseId)
                    .OrderBy(m => m.CreatedOnUtc);
            });
        }

        public virtual async Task<SupportCaseMessage> AddMessageAsync(int supportCaseId, int authorCustomerId, bool isStaff, string body)
        {
            var message = new SupportCaseMessage
            {
                SupportCaseId = supportCaseId,
                AuthorCustomerId = authorCustomerId,
                IsStaff = isStaff,
                Body = body,
                CreatedOnUtc = DateTime.UtcNow
            };
            await _messageRepository.InsertAsync(message);

            if (isStaff)
            {
                var supportCase = await GetSupportCaseByIdAsync(supportCaseId);
                if (supportCase != null)
                {
                    await NotifyCustomerAsync(
                        supportCase,
                        "New reply on your support case",
                        body);
                }
            }

            return message;
        }

        private async Task NotifyCustomerAsync(SupportCase supportCase, string title, string body)
        {
            await _pushNotificationService.SendNotificationAsync(
                supportCase.CustomerId,
                NotificationType.SupportCaseUpdate,
                title,
                body,
                new Dictionary<string, string>
                {
                    { "caseId", supportCase.Id.ToString() },
                    { "url", $"Support/{supportCase.Id}" }
                });
        }
    }
}
