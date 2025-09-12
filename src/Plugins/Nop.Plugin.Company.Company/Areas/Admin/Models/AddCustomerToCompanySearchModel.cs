using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    public partial record AddCustomerToCompanySearchModel : BaseSearchModel
    {
        [NopResourceDisplayName("Admin.Customer.Customers.List.SearchCustomerEmail")]
        public string SearchCustomerEmail { get; set; }

        public int CompanyId { get; set; }
    }
}
