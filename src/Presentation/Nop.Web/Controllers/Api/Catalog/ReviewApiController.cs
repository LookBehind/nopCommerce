using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Localization;

namespace Nop.Web.Controllers.Api.Catalog
{
    // Deliberately not [AuthorizeAttribute] - the storefront calls this from an anonymous
    // page script (cookie session, no JWT), and the options are static, non-sensitive text.
    [Produces("application/json")]
    [Route("api/review")]
    public partial class ReviewApiController : BaseApiController
    {
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;

        public ReviewApiController(ILocalizationService localizationService, IWorkContext workContext)
        {
            _localizationService = localizationService;
            _workContext = workContext;
        }

        //keys map to the LocaleStringResource entries seeded by AddReviewQuickOptionsLocalesMigration
        private static readonly string[] _quickOptionResourceNames =
        {
            "Reviews.QuickOption.ItemMismatch",
            "Reviews.QuickOption.PortionTooSmall",
            "Reviews.QuickOption.PoorTaste",
            "Reviews.QuickOption.NoteNotFollowed",
            "Reviews.QuickOption.PackagingDamaged",
            "Reviews.QuickOption.FoodSpilled",
            "Reviews.QuickOption.GreatTaste"
        };

        [HttpGet("quick-options")]
        public virtual async Task<IActionResult> GetQuickOptions()
        {
            var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;

            var options = new List<ReviewQuickOptionApiModel>();
            foreach (var resourceName in _quickOptionResourceNames)
            {
                options.Add(new ReviewQuickOptionApiModel
                {
                    Key = resourceName.Replace("Reviews.QuickOption.", string.Empty),
                    Text = await _localizationService.GetResourceAsync(resourceName, languageId)
                });
            }

            return Ok(options);
        }

        public partial class ReviewQuickOptionApiModel
        {
            public string Key { get; set; }
            public string Text { get; set; }
        }
    }
}
