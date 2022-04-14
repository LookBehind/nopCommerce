using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Web.Areas.Admin.Models.Vendors;
using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Companies
{
    public partial record AddVendorToCompanyListModel : BasePagedListModel<VendorModel>
    {
    }
}
