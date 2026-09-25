using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Company.Support.Areas.Admin.Models
{
    public partial record SupportCaseSearchModel : BaseSearchModel
    {
        public SupportCaseSearchModel()
        {
            AvailableStatuses = new List<SelectListItem>();
            AvailableCategories = new List<SelectListItem>();
        }

        [NopResourceDisplayName("Admin.Support.Cases.List.SearchStatus")]
        public int SearchStatusId { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.List.SearchCategory")]
        public int SearchCategoryId { get; set; }

        [NopResourceDisplayName("Admin.Support.Cases.List.SearchUnassignedOnly")]
        public bool SearchUnassignedOnly { get; set; }

        public IList<SelectListItem> AvailableStatuses { get; set; }

        public IList<SelectListItem> AvailableCategories { get; set; }
    }
}
