using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
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
