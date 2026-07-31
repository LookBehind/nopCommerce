using Nop.Web.Framework.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

public partial record AutoInviteUserSearchModel : BaseSearchModel
{
    public int StoreId { get; set; }
}
