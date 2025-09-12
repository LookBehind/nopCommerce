using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    /// <summary>
    /// Represents a company address list model
    /// </summary>
    public partial record CompanyAddressListModel : BasePagedListModel<CompanyAddressModel>
    {
    }
}
