using Nop.Web.Framework.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

public partial record VendorTelegramChatModel : BaseNopModel
{
    public int VendorId { get; set; }
    public string VendorName { get; set; }
    public int StoreId { get; set; }
    public string StoreName { get; set; }
    public string ChatTitle { get; set; }
    public long? ChatId { get; set; }
    public int? MessageThreadId { get; set; }

    /// <summary>
    /// True when this vendor is allowed for a company in this store but has no chat mapping -
    /// rendered as a warning row with a "Fix" button instead of chat details.
    /// </summary>
    public bool IsMissing { get; set; }
}
