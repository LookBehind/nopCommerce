using System;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Plugin.Notifications.Manager.Areas.Admin.Factories;
using Nop.Plugin.Notifications.Manager.Areas.Admin.Models;
using Nop.Plugin.Notifications.Manager.Services;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Filters;
using Telegram.Bot;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Controllers;

[Area(AreaNames.Admin)]
[AutoValidateAntiforgeryToken]
public class NotificationsManagerController : BaseAdminController
{
    private readonly INotificationsManagerModelFactory _modelFactory;
    private readonly ITelegramGroupProvisioningService _provisioningService;
    private readonly IVendorTelegramChatCache _chatCache;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IPermissionService _permissionService;
    private readonly IStoreContext _storeContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public NotificationsManagerController(
        INotificationsManagerModelFactory modelFactory,
        ITelegramGroupProvisioningService provisioningService,
        IVendorTelegramChatCache chatCache,
        ITelegramBotClient telegramBotClient,
        IPermissionService permissionService,
        IStoreContext storeContext,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _modelFactory = modelFactory;
        _provisioningService = provisioningService;
        _chatCache = chatCache;
        _telegramBotClient = telegramBotClient;
        _permissionService = permissionService;
        _storeContext = storeContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private async Task<int> GetActiveStoreIdAsync()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        if (storeScope > 0)
            return storeScope;

        return (await _storeContext.GetCurrentStoreAsync()).Id;
    }

    public async Task<IActionResult> Configure()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        var storeId = await GetActiveStoreIdAsync();

        var model = new ConfigurationModel();
        model.VendorTelegramChatSearchModel.StoreId = storeId;
        model.AutoInviteUserSearchModel.StoreId = storeId;

        model.VendorTelegramChatSearchModel = await _modelFactory.PrepareVendorTelegramChatSearchModelAsync(model.VendorTelegramChatSearchModel);
        model.AutoInviteUserSearchModel = await _modelFactory.PrepareAutoInviteUserSearchModelAsync(model.AutoInviteUserSearchModel);

        return View("~/Plugins/Notifications.Manager/Areas/Admin/Views/NotificationsManager/Configure.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> VendorChatList(VendorTelegramChatSearchModel searchModel)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return await AccessDeniedDataTablesJson();

        if (searchModel.StoreId == 0)
            searchModel.StoreId = await GetActiveStoreIdAsync();

        var model = await _modelFactory.PrepareVendorTelegramChatListModelAsync(searchModel);
        return Json(model);
    }

    [HttpPost]
    public async Task<IActionResult> AutoInviteUserList(AutoInviteUserSearchModel searchModel)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return await AccessDeniedDataTablesJson();

        if (searchModel.StoreId == 0)
            searchModel.StoreId = await GetActiveStoreIdAsync();

        var model = await _modelFactory.PrepareAutoInviteUserListModelAsync(searchModel);
        return Json(model);
    }

    [HttpPost]
    public async Task<IActionResult> Fix(int vendorId)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        var storeId = await GetActiveStoreIdAsync();

        // Resolved lazily (not constructor-injected) so a tenant without Hangfire's client wired up
        // yet can't break this controller for its unrelated actions - only clicking Fix could.
        var backgroundJobClient = _serviceProvider.GetRequiredService<IBackgroundJobClient>();
        backgroundJobClient.Enqueue<ITelegramGroupProvisioningService>(
            s => s.ProvisionVendorGroupAsync(vendorId, storeId));

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> RefreshChatNames()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        var storeId = await GetActiveStoreIdAsync();

        await _chatCache.EnsureLoadedAsync();
        var refreshed = 0;
        foreach (var kv in _chatCache.Snapshot.Where(kv => kv.Value.StoreId == storeId).ToList())
        {
            var vendor = kv.Value.Vendor;
            try
            {
                var existingTitle = await _chatCache.GetVendorChatTitleAsync(vendor, storeId);
                if (!string.IsNullOrEmpty(existingTitle))
                    continue;

                var chat = await _telegramBotClient.GetChat(kv.Key.ChatId);

                if (!string.IsNullOrEmpty(chat.Title))
                {
                    await _chatCache.SaveVendorChatTitleAsync(vendor, storeId, chat.Title);
                    refreshed++;
                }
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Failed to refresh Telegram chat name for vendor '{vendor.Name}'", e);
            }
        }

        return Json(new { success = true, refreshed });
    }

    [HttpPost]
    public async Task<IActionResult> AddAutoInviteUser(string username)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        if (string.IsNullOrWhiteSpace(username))
            return Json(new { success = false, message = "Username is required" });

        var storeId = await GetActiveStoreIdAsync();

        try
        {
            await _provisioningService.AddAutoInviteUserAsync(storeId, username);
            return Json(new { success = true });
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync($"Failed to add auto-invite user '@{username}'", e);
            return Json(new { success = false, message = e.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAutoInviteRemovalImpact(string username)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        var storeId = await GetActiveStoreIdAsync();
        var groupCount = await _provisioningService.GetGroupCountAsync(storeId);

        return Json(new { groupCount });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAutoInviteUser(string username)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
            return AccessDeniedView();

        var storeId = await GetActiveStoreIdAsync();

        try
        {
            await _provisioningService.RemoveAutoInviteUserAsync(storeId, username);
            return Json(new { success = true });
        }
        catch (Exception e)
        {
            await _logger.ErrorAsync($"Failed to remove auto-invite user '@{username}'", e);
            return Json(new { success = false, message = e.Message });
        }
    }
}
