using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Common;
using Nop.Services.News;
using Nop.Services.Stores;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Content
{
    /// <summary>
    /// mobile-v2's Home announcement carousel, backed by nopCommerce's own News items
    /// instead of a new custom entity - decided over building a dedicated Announcement
    /// entity because News already gives per-store scoping, Published +
    /// Start/EndDateUtc scheduling, and an admin CRUD screen an admin had already used
    /// (see 2026-09 chat: "when i added news through admin panel - it was shown on web
    /// store"). The one real gap (News has no background color/icon/manual sort field)
    /// is filled with three GenericAttributes on the NewsItem
    /// (NopNewsDefaults.AnnouncementBg/Icon/SortOrderAttribute) rather than a schema
    /// change - see AddAnnouncementFieldsToNewsItemLocalesMigration and the NewsController/
    /// NewsModelFactory admin-side wiring.
    ///
    /// The storefront's own News rendering (home page block, /news archive, RSS) was
    /// separately fully disabled (News settings > News enabled) so the same News items
    /// used here don't also show up unstyled on the web store - this endpoint is the
    /// only consumer of News data now.
    /// </summary>
    [Produces("application/json")]
    [Route("api/v2/announcements")]
    [Authorize]
    public class AnnouncementV2ApiController(
        INewsService newsService,
        IGenericAttributeService genericAttributeService,
        IStoreContext storeContext,
        IStoreMappingService storeMappingService)
        : BaseApiController
    {
        // Shown when an admin hasn't set a card color/icon for a given news item yet,
        // rather than sending mobile a blank background or an empty icon string.
        private const string DefaultBg = "#FDECC8";
        private const string DefaultIcon = "📣";

        public class AnnouncementV2Model
        {
            public int Id { get; set; }
            public string Bg { get; set; }
            public string Icon { get; set; }
            public string Title { get; set; }
            public string Sub { get; set; }
        }

        public class AnnouncementDetailV2Model : AnnouncementV2Model
        {
            public string Body { get; set; }
            public System.DateTime CreatedOnUtc { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetAnnouncements()
        {
            var store = await storeContext.GetCurrentStoreAsync();

            //Published + within Start/EndDateUtc + store-mapped, newest first (or by
            //StartDateUtc if set) - see NewsService.GetAllNewsAsync.
            var newsItems = await newsService.GetAllNewsAsync(storeId: store.Id, showHidden: false);

            var result = new List<(AnnouncementV2Model Model, int SortOrder)>(newsItems.Count);
            foreach (var newsItem in newsItems)
            {
                var bg = await genericAttributeService.GetAttributeAsync<string>(newsItem, NopNewsDefaults.AnnouncementBgAttribute);
                var icon = await genericAttributeService.GetAttributeAsync<string>(newsItem, NopNewsDefaults.AnnouncementIconAttribute);
                var sortOrder = await genericAttributeService.GetAttributeAsync<int>(newsItem, NopNewsDefaults.AnnouncementSortOrderAttribute);

                result.Add((new AnnouncementV2Model
                {
                    Id = newsItem.Id,
                    Bg = string.IsNullOrWhiteSpace(bg) ? DefaultBg : bg,
                    Icon = string.IsNullOrWhiteSpace(icon) ? DefaultIcon : icon,
                    Title = newsItem.Title,
                    Sub = newsItem.Short
                }, sortOrder));
            }

            // 0 (unset) sorts last, keeping GetAllNewsAsync's own newest-first order
            // among unsorted items (OrderBy is stable) - an admin who never touches
            // "Card sort order" gets the same behavior as before this field existed.
            return Ok(result
                .OrderBy(r => r.SortOrder == 0 ? int.MaxValue : r.SortOrder)
                .Select(r => r.Model)
                .ToList());
        }

        // Backs AnnouncementDetailScreen (the carousel card's onPress, previously a
        // stub toast) and mysnacksv2://News/:id deep links from a News-related push
        // notification, once one exists.
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAnnouncement(int id)
        {
            var store = await storeContext.GetCurrentStoreAsync();
            var newsItem = await newsService.GetNewsByIdAsync(id);
            if (newsItem == null || !newsItem.Published || !await storeMappingService.AuthorizeAsync(newsItem, store.Id))
                return NotFound();

            var bg = await genericAttributeService.GetAttributeAsync<string>(newsItem, NopNewsDefaults.AnnouncementBgAttribute);
            var icon = await genericAttributeService.GetAttributeAsync<string>(newsItem, NopNewsDefaults.AnnouncementIconAttribute);

            return Ok(new AnnouncementDetailV2Model
            {
                Id = newsItem.Id,
                Bg = string.IsNullOrWhiteSpace(bg) ? DefaultBg : bg,
                Icon = string.IsNullOrWhiteSpace(icon) ? DefaultIcon : icon,
                Title = newsItem.Title,
                Sub = newsItem.Short,
                Body = PlainTextFromHtml(newsItem.Full),
                CreatedOnUtc = newsItem.CreatedOnUtc
            });
        }

        // Same rationale/approach as CatalogV2ApiController's own copy of this method
        // (see that file) - News.Full is admin-authored rich HTML (TinyMCE) and mobile
        // renders it as plain RN <Text>, so raw markup can't reach the screen.
        private static string PlainTextFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var noTags = Regex.Replace(html, "<[^>]*>", " ");
            var decoded = WebUtility.HtmlDecode(noTags);
            var collapsed = Regex.Replace(decoded, @"\s+", " ").Trim();
            return collapsed.Length > 0 ? collapsed : null;
        }
    }
}
