using System.Collections.Generic;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Web.Areas.Admin.Models.Orders
{
    public partial class OrderScheduleModel
    {
        [NopResourceDisplayName("Admin.Orders.Fields.ScheduleDate")]
        public List<string> ScheduleDates { get; set; }
    }
}