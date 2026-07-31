using Nop.Web.Framework.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

public partial record VendorTelegramChatSearchModel : BaseSearchModel
{
    public int StoreId { get; set; }
}
