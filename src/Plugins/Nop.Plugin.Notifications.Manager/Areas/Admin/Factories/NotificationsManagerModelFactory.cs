using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Plugin.Notifications.Manager.Areas.Admin.Models;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Companies;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Web.Framework.Models.Extensions;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Factories;

public class NotificationsManagerModelFactory : INotificationsManagerModelFactory
{
    private readonly IVendorTelegramChatCache _chatCache;
    private readonly ITelegramGroupProvisioningService _provisioningService;
    private readonly ICompanyService _companyService;
    private readonly IVendorService _vendorService;
    private readonly IStoreService _storeService;

    public NotificationsManagerModelFactory(
        IVendorTelegramChatCache chatCache,
        ITelegramGroupProvisioningService provisioningService,
        ICompanyService companyService,
        IVendorService vendorService,
        IStoreService storeService)
    {
        _chatCache = chatCache;
        _provisioningService = provisioningService;
        _companyService = companyService;
        _vendorService = vendorService;
        _storeService = storeService;
    }

    public Task<VendorTelegramChatSearchModel> PrepareVendorTelegramChatSearchModelAsync(VendorTelegramChatSearchModel searchModel)
    {
        searchModel.SetGridPageSize();
        return Task.FromResult(searchModel);
    }

    public async Task<VendorTelegramChatListModel> PrepareVendorTelegramChatListModelAsync(VendorTelegramChatSearchModel searchModel)
    {
        var storeId = searchModel.StoreId;
        var store = await _storeService.GetStoreByIdAsync(storeId);

        await _chatCache.EnsureLoadedAsync();
        var mappedForStore = _chatCache.Snapshot.Where(kv => kv.Value.StoreId == storeId).ToList();

        var rows = new List<VendorTelegramChatModel>();
        var mappedVendorIds = new HashSet<int>();

        foreach (var kv in mappedForStore)
        {
            var vendor = kv.Value.Vendor;
            mappedVendorIds.Add(vendor.Id);

            rows.Add(new VendorTelegramChatModel
            {
                VendorId = vendor.Id,
                VendorName = vendor.Name,
                StoreId = storeId,
                StoreName = store?.Name,
                ChatTitle = await _chatCache.GetVendorChatTitleAsync(vendor, storeId),
                ChatId = kv.Key.ChatId,
                MessageThreadId = kv.Key.MessageThreadId,
                IsMissing = false
            });
        }

        var allowedVendorIds = new HashSet<int>();
        var companies = await _companyService.GetAllCompaniesAsync(storeId: storeId, pageSize: int.MaxValue);
        foreach (var company in companies)
        {
            var companyVendors = await _companyService.GetCompanyVendorsByCompanyAsync(company.Id);
            foreach (var companyVendor in companyVendors)
                allowedVendorIds.Add(companyVendor.VendorId);
        }

        foreach (var vendorId in allowedVendorIds.Except(mappedVendorIds))
        {
            var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
            if (vendor == null || vendor.Deleted)
                continue;

            rows.Add(new VendorTelegramChatModel
            {
                VendorId = vendor.Id,
                VendorName = vendor.Name,
                StoreId = storeId,
                StoreName = store?.Name,
                ChatTitle = null,
                ChatId = null,
                MessageThreadId = null,
                IsMissing = true
            });
        }

        rows = rows.OrderBy(r => r.VendorName).ToList();

        var pagedRows = new PagedList<VendorTelegramChatModel>(rows, searchModel.Page - 1, searchModel.PageSize);

        var model = await new VendorTelegramChatListModel().PrepareToGridAsync(searchModel, pagedRows,
            () => pagedRows.ToAsyncEnumerable());

        return model;
    }

    public Task<AutoInviteUserSearchModel> PrepareAutoInviteUserSearchModelAsync(AutoInviteUserSearchModel searchModel)
    {
        searchModel.SetGridPageSize();
        return Task.FromResult(searchModel);
    }

    public async Task<AutoInviteUserListModel> PrepareAutoInviteUserListModelAsync(AutoInviteUserSearchModel searchModel)
    {
        var storeId = searchModel.StoreId;

        // Not configured for this tenant (no MTProto session) - show an empty grid rather than
        // calling into the Null service, which throws by design for anything that skips this check.
        var rows = _provisioningService.IsConfigured
            ? (await _provisioningService.GetAutoInviteEntriesAsync(storeId))
                .Select(entry => new AutoInviteUserModel { Identifier = entry.Identifier, DisplayName = entry.DisplayName, StoreId = storeId })
                .OrderBy(m => m.DisplayName)
                .ToList()
            : new List<AutoInviteUserModel>();

        var pagedRows = new PagedList<AutoInviteUserModel>(rows, searchModel.Page - 1, searchModel.PageSize);

        var model = await new AutoInviteUserListModel().PrepareToGridAsync(searchModel, pagedRows,
            () => pagedRows.ToAsyncEnumerable());

        return model;
    }
}
