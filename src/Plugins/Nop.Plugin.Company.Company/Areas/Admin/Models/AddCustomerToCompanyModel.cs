using System.Collections.Generic;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    public partial record AddCustomerToCompanyModel : BaseNopModel
    {
        public AddCustomerToCompanyModel()
        {
            SelectedCustomerIds = new List<int>();
        }
        public int CompanyId { get; set; }
        public IList<int> SelectedCustomerIds { get; set; }
    }
}
