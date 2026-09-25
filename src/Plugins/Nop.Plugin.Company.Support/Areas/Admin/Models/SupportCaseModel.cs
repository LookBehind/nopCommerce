using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Company.Support.Areas.Admin.Models
{
    public partial record SupportCaseModel : BaseNopEntityModel
    {
        public SupportCaseModel()
        {
            AvailableStatuses = new List<SelectListItem>();
            StatusHistory = new List<SupportCaseStatusHistoryModel>();
        }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.Customer")]
        public string CustomerName { get; set; }

        public int CustomerId { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.Category")]
        public string CategoryName { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.Vendor")]
        public string VendorName { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.Subject")]
        public string Subject { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.Description")]
        public string Description { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.Status")]
        public int StatusId { get; set; }

        public string StatusName { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.AssignedTo")]
        public string AssignedToName { get; set; }

        public bool IsAssigned { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.Fields.CreatedOn")]
        public DateTime CreatedOnUtc { get; set; }

        public DateTime UpdatedOnUtc { get; set; }

        public IList<SelectListItem> AvailableStatuses { get; set; }

        public IList<SupportCaseStatusHistoryModel> StatusHistory { get; set; }
    }

    public partial record SupportCaseStatusHistoryModel
    {
        [NopResourceDisplayName("Admin.Support.Cases.StatusHistory.Status")]
        public string StatusName { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.StatusHistory.EnteredOn")]
        public DateTime EnteredOnUtc { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.StatusHistory.Duration")]
        public string Duration { get; set; }
    }
}
