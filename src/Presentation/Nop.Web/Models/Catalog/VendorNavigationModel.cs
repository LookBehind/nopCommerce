using System.Collections.Generic;
using Nop.Web.Framework.Models;

namespace Nop.Web.Models.Catalog
{
    public partial record VendorNavigationModel : BaseNopModel
    {
        public VendorNavigationModel()
        {
            Vendors = new List<VendorBriefInfoModel>();
        }

        public IList<VendorBriefInfoModel> Vendors { get; set; }

        public int TotalVendors { get; set; }
    }

    public partial record VendorBriefInfoModel : BaseNopEntityModel
    {
        public string Name { get; set; }

        public string SeName { get; set; }

        public string PictureUrl { get; set; }

        /// <summary>
        /// Sum of ratings (1-5) across all approved reviews of this vendor's products.
        /// Divide by <see cref="TotalReviews"/> for the average, mirroring how product-level
        /// rating is exposed (RatingSum/TotalReviews) rather than a pre-divided average.
        /// </summary>
        public int RatingSum { get; set; }

        /// <summary>
        /// Count of approved reviews across all of this vendor's products.
        /// </summary>
        public int TotalReviews { get; set; }
    }
}