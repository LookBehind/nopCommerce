using System;
using System.Collections.Generic;

namespace Nop.Web.Models.Api.Catalog
{
    /// <summary>
    /// Response envelope for the mobile "product reviews" drawer:
    /// rating-score distribution + the list of individual reviews.
    /// </summary>
    public class ProductReviewsResponseApiModel
    {
        public ProductReviewsResponseApiModel()
        {
            Distribution = new Dictionary<int, int>();
            Reviews = new List<ProductReviewItemApiModel>();
        }

        public bool Success { get; set; }
        public string Message { get; set; }
        public int ProductId { get; set; }

        //average of approved ratings, rounded to one decimal (0 when no reviews)
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }

        //count of approved reviews per star (keys 1..5, zero-filled)
        public Dictionary<int, int> Distribution { get; set; }

        //the current customer's own (latest) review id, for client-side highlighting (null = none)
        public int? CurrentUserReviewId { get; set; }

        public List<ProductReviewItemApiModel> Reviews { get; set; }
    }

    public class ProductReviewItemApiModel
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }

        //reviewer display name with all but the first 3 letters masked (server-side, for privacy)
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public int Rating { get; set; }
        public string Title { get; set; }
        public string ReviewText { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public bool IsCurrentUser { get; set; }
    }
}
