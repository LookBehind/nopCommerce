using Nop.Core.Caching;

namespace Nop.Services.News
{
    /// <summary>
    /// Represents default values related to orders services
    /// </summary>
    public static partial class NopNewsDefaults
    {
        #region Generic attributes

        /// <summary>
        /// Gets a name of a generic attribute to store a news item's mobile-v2 announcement
        /// card background color (hex string, e.g. "#FDECC8") - not a NewsItem entity
        /// column, see AddAnnouncementFieldsToNewsItemLocalesMigration.
        /// </summary>
        public static string AnnouncementBgAttribute => "AnnouncementBg";

        /// <summary>
        /// Gets a name of a generic attribute to store a news item's mobile-v2 announcement
        /// card icon (a single emoji) - not a NewsItem entity column.
        /// </summary>
        public static string AnnouncementIconAttribute => "AnnouncementIcon";

        /// <summary>
        /// Gets a name of a generic attribute to store a news item's mobile-v2 announcement
        /// card manual sort order (lower shows first; 0 falls back to newest-first) - not a
        /// NewsItem entity column.
        /// </summary>
        public static string AnnouncementSortOrderAttribute => "AnnouncementSortOrder";

        #endregion

        #region Caching defaults

        /// <summary>
        /// Key for number of news comments
        /// </summary>
        /// <remarks>
        /// {0} : news item ID
        /// {1} : store ID
        /// {2} : are only approved comments?
        /// </remarks>
        public static CacheKey NewsCommentsNumberCacheKey => new CacheKey("Nop.newsitem.comments.number.{0}-{1}-{2}", NewsCommentsNumberPrefix);

        /// <summary>
        /// Gets a key pattern to clear cache
        /// </summary>
        /// <remarks>
        /// {0} : news item ID
        /// </remarks>
        public static string NewsCommentsNumberPrefix => "Nop.newsitem.comments.number.{0}";

        #endregion
    }
}