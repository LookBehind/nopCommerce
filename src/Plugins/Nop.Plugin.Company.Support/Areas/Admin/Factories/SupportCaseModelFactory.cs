using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Plugin.Company.Support.Areas.Admin.Models;
using Nop.Plugin.Company.Support.Domain;
using Nop.Plugin.Company.Support.Services;
using Nop.Services.Customers;
using Nop.Services.Vendors;
using Nop.Web.Framework.Extensions;
using Nop.Web.Framework.Models.Extensions;

namespace Nop.Plugin.Company.Support.Areas.Admin.Factories
{
    public class SupportCaseModelFactory : ISupportCaseModelFactory
    {
        private readonly ISupportCaseService _supportCaseService;
        private readonly ICustomerService _customerService;
        private readonly IVendorService _vendorService;

        public SupportCaseModelFactory(
            ISupportCaseService supportCaseService,
            ICustomerService customerService,
            IVendorService vendorService)
        {
            _supportCaseService = supportCaseService;
            _customerService = customerService;
            _vendorService = vendorService;
        }

        private static IList<SelectListItem> StatusSelectList(int? selectedId = null)
        {
            var items = SupportCaseDisplayNames.Status
                .Select(kv => new SelectListItem
                {
                    Text = kv.Value,
                    Value = ((int)kv.Key).ToString(),
                    Selected = selectedId.HasValue && (int)kv.Key == selectedId.Value
                })
                .ToList();
            return items;
        }

        private static IList<SelectListItem> CategorySelectList(int? selectedId = null)
        {
            var items = new List<SelectListItem>
            {
                new SelectListItem { Text = "All", Value = "0" }
            };
            items.AddRange(SupportCaseDisplayNames.Category
                .Select(kv => new SelectListItem
                {
                    Text = kv.Value,
                    Value = ((int)kv.Key).ToString(),
                    Selected = selectedId.HasValue && (int)kv.Key == selectedId.Value
                }));
            return items;
        }

        public virtual async Task<SupportCaseSearchModel> PrepareSupportCaseSearchModelAsync(SupportCaseSearchModel searchModel)
        {
            if (searchModel == null)
                throw new ArgumentNullException(nameof(searchModel));

            searchModel.AvailableStatuses = StatusSelectList();
            searchModel.AvailableStatuses.Insert(0, new SelectListItem { Text = "All", Value = "0" });
            searchModel.AvailableCategories = CategorySelectList();

            searchModel.SetGridPageSize();

            return searchModel;
        }

        public virtual async Task<SupportCaseListModel> PrepareSupportCaseListModelAsync(SupportCaseSearchModel searchModel)
        {
            if (searchModel == null)
                throw new ArgumentNullException(nameof(searchModel));

            var customerIds = await ResolveCustomerIdsAsync(searchModel.SearchCustomerEmailOrName);

            var cases = await _supportCaseService.SearchSupportCasesAsync(
                statusId: searchModel.SearchStatusId > 0 ? searchModel.SearchStatusId : null,
                categoryId: searchModel.SearchCategoryId > 0 ? searchModel.SearchCategoryId : null,
                unassignedOnly: searchModel.SearchUnassignedOnly,
                customerIds: customerIds,
                pageIndex: searchModel.Page - 1,
                pageSize: searchModel.PageSize);

            var model = await new SupportCaseListModel().PrepareToGridAsync(searchModel, cases, () =>
                cases.SelectAwait(async supportCase => await PrepareSupportCaseModelAsync(new SupportCaseModel(), supportCase)));

            return model;
        }

        /// <summary>
        /// Resolves a free-text "name or email" search term into matching customer ids - OR'd
        /// across email/first name/last name (each independently, via .Contains matching, same
        /// as ICustomerService's own email/firstName/lastName params), plus a "First Last" (and
        /// reversed "Last First") combo attempt for two-word input, since GetAllCustomersAsync
        /// only ANDs its own params together and can't do a single free-text OR search itself.
        /// Returns null (no filter) for a blank term, or a possibly-empty array (a real "matched
        /// nobody" filter) otherwise.
        /// </summary>
        private async Task<int[]> ResolveCustomerIdsAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return null;

            term = term.Trim();
            var ids = new HashSet<int>();

            async Task AddMatches(string email = null, string firstName = null, string lastName = null)
            {
                var matches = await _customerService.GetAllCustomersAsync(
                    email: email, firstName: firstName, lastName: lastName, pageSize: int.MaxValue);
                foreach (var customer in matches)
                    ids.Add(customer.Id);
            }

            await AddMatches(email: term);
            await AddMatches(firstName: term);
            await AddMatches(lastName: term);

            var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                await AddMatches(firstName: words[0], lastName: string.Join(' ', words.Skip(1)));
                await AddMatches(firstName: words[^1], lastName: string.Join(' ', words.Take(words.Length - 1)));
            }

            return ids.ToArray();
        }

        public virtual async Task<SupportCaseModel> PrepareSupportCaseModelAsync(SupportCaseModel model, SupportCase supportCase)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (supportCase == null)
                return model;

            model.Id = supportCase.Id;
            model.CustomerId = supportCase.CustomerId;
            model.Subject = supportCase.Subject;
            model.Description = supportCase.Description;
            model.StatusId = supportCase.StatusId;
            model.StatusName = SupportCaseDisplayNames.Status.TryGetValue(supportCase.Status, out var statusName)
                ? statusName
                : supportCase.Status.ToString();
            model.CategoryName = SupportCaseDisplayNames.Category.TryGetValue(supportCase.Category, out var categoryName)
                ? categoryName
                : supportCase.Category.ToString();
            model.CreatedOnUtc = supportCase.CreatedOnUtc;
            model.UpdatedOnUtc = supportCase.UpdatedOnUtc;
            model.IsUnread = supportCase.IsUnreadByStaff();

            var customer = await _customerService.GetCustomerByIdAsync(supportCase.CustomerId);
            if (customer != null)
            {
                var fullName = await _customerService.GetCustomerFullNameAsync(customer);
                model.CustomerName = string.IsNullOrWhiteSpace(fullName) ? customer.Email : $"{fullName} ({customer.Email})";
            }

            if (supportCase.VendorId.HasValue)
            {
                var vendor = await _vendorService.GetVendorByIdAsync(supportCase.VendorId.Value);
                model.VendorName = vendor?.Name;
            }

            model.IsAssigned = supportCase.AssignedToCustomerId.HasValue;
            if (supportCase.AssignedToCustomerId.HasValue)
            {
                var assignee = await _customerService.GetCustomerByIdAsync(supportCase.AssignedToCustomerId.Value);
                if (assignee != null)
                {
                    var assigneeFullName = await _customerService.GetCustomerFullNameAsync(assignee);
                    model.AssignedToName = string.IsNullOrWhiteSpace(assigneeFullName) ? assignee.Email : assigneeFullName;
                }
            }

            model.AvailableStatuses = StatusSelectList(model.StatusId);

            var history = await _supportCaseService.GetStatusHistoryAsync(supportCase.Id);
            for (var i = 0; i < history.Count; i++)
            {
                var row = history[i];
                var endsAt = i + 1 < history.Count ? history[i + 1].EnteredOnUtc : DateTime.UtcNow;
                var duration = endsAt - row.EnteredOnUtc;

                model.StatusHistory.Add(new SupportCaseStatusHistoryModel
                {
                    StatusName = SupportCaseDisplayNames.Status.TryGetValue(row.Status, out var rowStatusName)
                        ? rowStatusName
                        : row.Status.ToString(),
                    EnteredOnUtc = row.EnteredOnUtc,
                    Duration = FormatDuration(duration)
                });
            }

            var messages = await _supportCaseService.GetMessagesAsync(supportCase.Id);
            var authorNames = new Dictionary<int, string>();
            foreach (var message in messages)
            {
                if (!authorNames.TryGetValue(message.AuthorCustomerId, out var authorName))
                {
                    var author = await _customerService.GetCustomerByIdAsync(message.AuthorCustomerId);
                    if (author == null)
                    {
                        authorName = "Unknown";
                    }
                    else
                    {
                        var authorFullName = await _customerService.GetCustomerFullNameAsync(author);
                        authorName = string.IsNullOrWhiteSpace(authorFullName) ? author.Email : authorFullName;
                    }
                    authorNames[message.AuthorCustomerId] = authorName;
                }

                model.Messages.Add(new SupportCaseMessageModel
                {
                    AuthorName = authorName,
                    IsStaff = message.IsStaff,
                    Body = message.Body,
                    CreatedOnUtc = message.CreatedOnUtc
                });
            }

            return model;
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalDays >= 1)
                return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{(int)Math.Max(span.TotalMinutes, 0)}m";
        }
    }
}
