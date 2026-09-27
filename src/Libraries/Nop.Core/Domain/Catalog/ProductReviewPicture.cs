namespace Nop.Core.Domain.Catalog
{
    /// <summary>
    /// Links a ProductReview to a Picture - a review can have several. MySnacks addition
    /// (core nopCommerce has no built-in concept of review photos).
    /// </summary>
    public partial class ProductReviewPicture : BaseEntity
    {
        /// <summary>
        /// Gets or sets the owning review's identifier
        /// </summary>
        public int ProductReviewId { get; set; }

        /// <summary>
        /// Gets or sets the picture identifier
        /// </summary>
        public int PictureId { get; set; }

        /// <summary>
        /// Gets or sets the display order among the review's own photos
        /// </summary>
        public int DisplayOrder { get; set; }
    }
}
