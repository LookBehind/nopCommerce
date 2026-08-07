using System.Collections.Generic;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    /// <summary>
    /// The weekly working-day pattern for a company-vendor pair, shown in the Schedule popup
    /// </summary>
    public partial record CompanyVendorScheduleModel : BaseNopModel
    {
        public int CompanyId { get; set; }

        public int VendorId { get; set; }

        public string VendorName { get; set; }

        /// <summary>
        /// The configured working days (System.DayOfWeek numeric values). Empty means the
        /// vendor is available every day.
        /// </summary>
        public IList<int> WorkingDays { get; set; } = new List<int>();
    }

    /// <summary>
    /// A single day's off-status, used by the calendar's per-month AJAX response
    /// </summary>
    public partial record CompanyVendorDayOffModel : BaseNopModel
    {
        /// <summary>
        /// Date in "yyyy-MM-dd" format
        /// </summary>
        public string Date { get; set; }

        public bool IsOff { get; set; }
    }
}
