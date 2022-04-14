using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Companies
{
    public partial record AddVendorToCompanyModel : BaseNopModel
    {
        public AddVendorToCompanyModel()
        {
            SelectedVendorIds = new List<int>();
        }
        public int CompanyId { get; set; }
        public IList<int> SelectedVendorIds { get; set; }
    }
}
