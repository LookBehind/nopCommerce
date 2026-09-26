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
            Messages = new List<SupportCaseMessageModel>();
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

        public IList<SupportCaseMessageModel> Messages { get; set; }

        /// <summary>
        /// Bound from the reply textarea on submit - not part of the case itself, just the
        /// draft text of a new staff message (see SupportCaseController.AddMessage).
        /// </summary>
        public string NewMessageBody { get; set; }
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

    public partial record SupportCaseMessageModel
    {
        public string AuthorName { get; set; }

        public bool IsStaff { get; set; }

        public string Body { get; set; }

        public DateTime CreatedOnUtc { get; set; }
    }
}
