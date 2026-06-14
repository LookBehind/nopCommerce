using System;
using System.Collections.Generic;
using Nop.Plugin.Company.Company.Services.Reporting;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Models.Reporting
{
    public record ReportsDashboardModel : BaseNopModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public bool IsAdmin { get; set; }
        public IList<ReportResult> Widgets { get; set; } = new List<ReportResult>();
    }
}
