using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Companies
{
    public partial record AddVendorToCompanySearchModel : BaseSearchModel
    {
        public string SearchVendorEmail { get; set; }

        public int CompanyId { get; set; }
    }
}
