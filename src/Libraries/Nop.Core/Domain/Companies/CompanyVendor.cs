using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Core.Domain.Companies
{
    public partial class CompanyVendor : BaseEntity
    {
        public int CompanyId { get; set; }
        public int VendorId { get; set; }
    }
}
