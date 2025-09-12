using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{

    public partial record CompanyVendorModel : BaseNopEntityModel
    {
        public int CompanyId { get; set; }

        public int VendorId { get; set; }

        public string VendorName { get; set; }

        public string Email { get; set; }
    }
}
