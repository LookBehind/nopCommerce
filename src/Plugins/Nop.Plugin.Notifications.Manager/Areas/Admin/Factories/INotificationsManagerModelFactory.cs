using System.Threading.Tasks;
using Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Factories;

public interface INotificationsManagerModelFactory
{
    Task<VendorTelegramChatSearchModel> PrepareVendorTelegramChatSearchModelAsync(VendorTelegramChatSearchModel searchModel);

    /// <summary>
    /// Builds the union of vendors with an existing chat mapping and company-allowed vendors that
    /// are missing one, for the active store.
    /// </summary>
    Task<VendorTelegramChatListModel> PrepareVendorTelegramChatListModelAsync(VendorTelegramChatSearchModel searchModel);

    Task<AutoInviteUserSearchModel> PrepareAutoInviteUserSearchModelAsync(AutoInviteUserSearchModel searchModel);
    Task<AutoInviteUserListModel> PrepareAutoInviteUserListModelAsync(AutoInviteUserSearchModel searchModel);
}
