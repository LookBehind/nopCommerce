using System.Collections.Generic;
using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Orders
{
    public partial class OrderScheduleModel
    {
        public List<DeliverySlotModel> Slots { get; set; } = new();
    }

    public partial class DeliverySlotModel
    {
        public string OpenTime { get; set; }
        public string CutoffTime { get; set; }
        public string DeliveryTime { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
