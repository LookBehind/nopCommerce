using Nop.Web.Framework.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

public partial record ConfigurationModel : BaseNopModel
{
    public ConfigurationModel()
    {
        VendorTelegramChatSearchModel = new VendorTelegramChatSearchModel();
        AutoInviteUserSearchModel = new AutoInviteUserSearchModel();
    }

    public VendorTelegramChatSearchModel VendorTelegramChatSearchModel { get; set; }
    public AutoInviteUserSearchModel AutoInviteUserSearchModel { get; set; }
}
