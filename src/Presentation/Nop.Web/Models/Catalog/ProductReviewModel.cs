using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Web.Models.Catalog
{
    public partial record ProductReviewOverviewModel : BaseNopModel
    {
        public int ProductId { get; set; }

        public int RatingSum { get; set; }

        public int TotalReviews { get; set; }
        
        public bool AllowCustomerReviews { get; set; }

        public bool CanAddNewReview { get; set; }
    }

    public partial record ProductReviewsModel : BaseNopModel
    {
        public ProductReviewsModel()
        {
            Items = new List<ProductReviewModel>();
            AddProductReview = new AddProductReviewModel();
            ReviewTypeList = new List<ReviewTypeModel>();
            AddAdditionalProductReviewList = new List<AddProductReviewReviewTypeMappingModel>();
        }

        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public string ProductSeName { get; set; }

        public IList<ProductReviewModel> Items { get; set; }

        public AddProductReviewModel AddProductReview { get; set; }

        public IList<ReviewTypeModel> ReviewTypeList { get; set; }

        public IList<AddProductReviewReviewTypeMappingModel> AddAdditionalProductReviewList { get; set; }        
    }

    public partial record ReviewTypeModel : BaseNopEntityModel
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsRequired { get; set; }

        public bool VisibleToAllCustomers { get; set; }

        public double AverageRating { get; set; }
    }

    public partial record ProductReviewModel : BaseNopEntityModel
    {
        public ProductReviewModel()
        {
            AdditionalProductReviewList = new List<ProductReviewReviewTypeMappingModel>();
        }

        public int CustomerId { get; set; }

        public string CustomerAvatarUrl { get; set; }

        public string CustomerName { get; set; }

        public bool AllowViewingProfiles { get; set; }
        
        public string Title { get; set; }

        public string ReviewText { get; set; }

        public string ReplyText { get; set; }

        public int Rating { get; set; }

        public string WrittenOnStr { get; set; }

        public ProductReviewHelpfulnessModel Helpfulness { get; set; }

        public IList<ProductReviewReviewTypeMappingModel> AdditionalProductReviewList { get; set; }
    }

    public partial record ProductReviewHelpfulnessModel : BaseNopModel
    {
        public int ProductReviewId { get; set; }

        public int HelpfulYesTotal { get; set; }

        public int HelpfulNoTotal { get; set; }
    }

    public partial record AddProductReviewModel : BaseNopModel
    {
        public AddProductReviewModel()
        {
            OrderItemOptions = new List<SelectListItem>();
        }

        [NopResourceDisplayName("Reviews.Fields.Title")]
        public string Title { get; set; }

        [NopResourceDisplayName("Reviews.Fields.ReviewText")]
        public string ReviewText { get; set; }

        [NopResourceDisplayName("Reviews.Fields.Rating")]
        public int Rating { get; set; }

        public bool DisplayCaptcha { get; set; }

        public bool CanCurrentCustomerLeaveReview { get; set; }

        public bool SuccessfullyAdded { get; set; }

        public bool CanAddNewReview { get; set; }

        public string Result { get; set; }

        /// <summary>
        /// The order item being reviewed - resolved server-side when the customer has exactly one
        /// eligible (non-cancelled, unreviewed) order item for this product, otherwise bound from
        /// the picker below. Re-validated server-side on submit regardless of the value posted.
        /// </summary>
        [NopResourceDisplayName("Reviews.SelectOrderToReview")]
        public int? OrderItemId { get; set; }

        /// <summary>
        /// Populated only when the customer has more than one eligible order item for this
        /// product, so the storefront can show a "which order?" picker.
        /// </summary>
        public IList<SelectListItem> OrderItemOptions { get; set; }
    }

    public partial record AddProductReviewReviewTypeMappingModel : BaseNopEntityModel
    {
        public int ProductReviewId { get; set; }

        public int ReviewTypeId { get; set; }

        public int Rating { get; set; }
        
        public string Name { get; set; }

        public string Description { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsRequired { get; set; }
    }

    public partial record ProductReviewReviewTypeMappingModel : BaseNopEntityModel
    {
        public int ProductReviewId { get; set; }

        public int ReviewTypeId { get; set; }

        public int Rating { get; set; }

        public string Name { get; set; }

        public bool VisibleToAllCustomers { get; set; }
    }
}