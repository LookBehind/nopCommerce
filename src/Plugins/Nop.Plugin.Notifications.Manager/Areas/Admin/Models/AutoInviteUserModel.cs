using Nop.Web.Framework.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

public partial record AutoInviteUserModel : BaseNopModel
{
    public string Username { get; set; }
    public int StoreId { get; set; }
}
