using Nop.Web.Framework.Models;

namespace Nop.Plugin.Notifications.Manager.Areas.Admin.Models;

public partial record AutoInviteUserModel : BaseNopModel
{
    /// <summary>
    /// What was typed to add this person - a "@username" or a phone number. Passed back to the
    /// Remove action, since removal re-resolves via this same identifier.
    /// </summary>
    public string Identifier { get; set; }

    /// <summary>
    /// Resolved Telegram display name, captured when this entry was added.
    /// </summary>
    public string DisplayName { get; set; }

    public int StoreId { get; set; }
}
