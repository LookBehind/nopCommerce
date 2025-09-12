using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    public partial record CompanyCustomerSearchModel : BaseSearchModel
    {
        public int CompanyId { get; set; }
    }
}
