using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Vendors;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Catalog
{
    /// <summary>
    /// mobile-v2's own catalog read surface - versioned separately from CatalogApiController's
    /// api/catalog (the v1 mobile app's production endpoints) so this can be shaped exactly
    /// for mobile-v2's ProductOverviewApiModel/VendorBriefModel contract (see
    /// mysnacks-mobile-v2/src/store/services/types.ts) without any risk of touching what v1
    /// depends on. api/v1 and api/v3 are both unused as of this pass - v2 chosen since it's
    /// the first free slot, matching this app's own version.
    ///
    /// Deliberately NOT reusing CatalogApiController.PrepareApiProductOverviewModels: that
    /// method drives a full IProductModelFactory.PrepareProductDetailsModelAsync pass per
    /// product (breadcrumbs, full price calc, review overview) for a v1 response shape with
    /// many fields mobile-v2 doesn't use. mobile-v2's ProductOverviewApiModel is a small
    /// subset, so this maps directly off the Product entity + a few targeted service calls
    /// instead - cheaper, and avoids inheriting from a controller full of unrelated actions
    /// just to reach a protected helper.
    ///
    /// No pagination on GET products - the whole point of this pass (see mobile's
    /// DiscoverScreen/curatedSections.ts/discoverSearch.ts) is fetching the full catalog once
    /// and keeping the existing client-side search/filter/curation logic working unchanged
    /// against real data instead of mock fixtures. api/catalog/product-search remains
    /// available for real server-side paginated search if that's ever needed.
    ///
    /// GetProducts() found live on mysnacks-dev's actual catalog (1060 products, not the
    /// mock's toy dataset) that a naive per-product sequential-await mapping (picture lookup,
    /// category lookup, spec-attribute lookup with two MORE sequential lookups per mapped
    /// value, a fresh vendor-rating aggregation, a fresh bestsellers report - all re-fetched
    /// per product) took 37+ seconds end to end - unusable. Fixed by hoisting everything that
    /// doesn't vary per product (vendor ratings, vendor popularity, category names, the
    /// Ingredients taxonomy's option-id-to-name map) into single upfront passes over the much
    /// smaller vendor/category/attribute-option lists, then mapping products with bounded
    /// parallelism (the remaining per-product calls - picture, product-category mapping,
    /// product-specification mapping - are genuinely independent I/O once the shared lookups
    /// are precomputed).
    [Produces("application/json")]
    [Route("api/v2/catalog")]
    [Authorize]
    public class CatalogV2ApiController(
        IProductService productService,
        ICategoryService categoryService,
        IVendorService vendorService,
        ISpecificationAttributeService specificationAttributeService,
        IPictureService pictureService,
        IOrderReportService orderReportService,
        IPriceFormatter priceFormatter,
        IStoreContext storeContext,
        IProductAttributeService productAttributeService)
        : BaseApiController
    {
        private const string IngredientsAttributeName = "Ingredients";
        private const int ThumbnailSize = 300;
        // Bounds how many products are mapped concurrently - unbounded Task.WhenAll over
        // 1000+ products would open that many simultaneous DB connections/requests at once.
        private const int ProductMapConcurrency = 32;

        public class ProductOverviewV2Model
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal PriceValue { get; set; }
            // Named to match the real v1 CatalogApiController's own
            // ProductOverviewApiModel.Price field (Models/Api/Catalog/
            // ProductOverviewApiModel.cs) - same field name, same meaning (fully
            // backend-formatted via IPriceFormatter, this store's actual configured
            // currency, e.g. Armenian Dram's "#,##0 ֏" - not "$0.00"), so mobile's
            // shared ProductOverviewApiModel TS type works the same way whether a
            // product came from here or from the real api/catalog/favourites
            // endpoint. PriceValue stays a plain decimal for anything doing real
            // math with it (cart totals, etc).
            public string Price { get; set; }
            public string ImageUrl { get; set; }
            public string CategoryName { get; set; }
            public bool RibbonEnable { get; set; }
            public string RibbonText { get; set; }
            public int RatingSum { get; set; }
            public int TotalReviews { get; set; }
            // Real order-driven signal (total quantity sold, per vendor's bestsellers
            // report) - not a fabricated "trending" flag. Backs Discover's "Getting
            // popular" curated row; will be genuinely flat/tied on a tenant with little
            // or no real order history yet, which is an honest reflection of that, not
            // a bug in this endpoint.
            public int PopularityCount { get; set; }
            public VendorBriefV2Model Vendor { get; set; }
            public string Description { get; set; }
            public IList<string> SpecificationLabels { get; set; } = new List<string>();
            // Whether GET products/{id}/attributes is worth calling before adding to
            // cart - lets a product card/QuantityStepper branch to an attribute-picker
            // sheet instead of an instant add without a second round trip just to find
            // out. ~20% of the real catalog has at least one mapping (not a rare case).
            public bool HasAttributes { get; set; }
        }

        public class ProductAttributeValueV2Model
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsPreSelected { get; set; }
            // Plain catalog-defined adjustment (ProductAttributeValue.PriceAdjustment/
            // PriceAdjustmentUsePercentage), not IPriceCalculationService's
            // customer/discount-aware variant - this is a pre-add-to-cart picker with
            // no cart context yet, and the real unit price (attribute-adjusted, via
            // IShoppingCartService.GetUnitPriceAsync) is what api/v2/cart already
            // returns once the item is actually in the cart.
            public string PriceAdjustmentFormatted { get; set; }
        }

        public class ProductAttributeMappingV2Model
        {
            public int MappingId { get; set; }
            public string Name { get; set; }
            public bool IsRequired { get; set; }
            // AttributeControlType's name (RadioList/Checkboxes/DropdownList/...) so
            // mobile can switch on a plain string instead of duplicating nopCommerce's
            // int enum values.
            public string ControlType { get; set; }
            public IList<ProductAttributeValueV2Model> Values { get; set; } = new List<ProductAttributeValueV2Model>();
        }

        public class VendorBriefV2Model
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PictureUrl { get; set; }
            public int RatingSum { get; set; }
            public int TotalReviews { get; set; }
            public string Description { get; set; }
        }

        public class CategoryV2Model
        {
            public string Name { get; set; }
            public int Count { get; set; }
            public string Description { get; set; }
            public string PictureUrl { get; set; }
        }

        public class IngredientV2Model
        {
            public string Name { get; set; }
            public bool IsAllergen { get; set; }
        }

        [HttpGet("products")]
        public async Task<IActionResult> GetProducts()
        {
            return Ok(await BuildProductOverviewsAsync());
        }

        // Curated home/Discover rows, computed server-side so mobile's
        // curatedSections.ts doesn't have to duplicate this logic (or risk
        // drifting from it) client-side - "new"/"trending"/"toprated" mirror
        // mobile's own CuratedSectionKey exactly. Shares BuildProductOverviewsAsync
        // with the plain product list rather than re-mapping products from
        // scratch; the filter/sort/take here is the exact same logic
        // curatedSections.ts used to do client-side.
        [HttpGet("curated/{section}")]
        public async Task<IActionResult> GetCuratedProducts(string section)
        {
            var products = await BuildProductOverviewsAsync();

            IEnumerable<ProductOverviewV2Model> curated = section?.ToLowerInvariant() switch
            {
                "new" => products.Where(p => p.RibbonEnable && p.RibbonText == "new"),
                "trending" => products.OrderByDescending(p => p.PopularityCount).Take(8),
                "toprated" => products.OrderByDescending(p => VendorAverageRating(p.Vendor)).Take(4),
                _ => null
            };

            if (curated == null)
                return BadRequest(new { message = $"Unknown curated section '{section}'. Expected new, trending, or toprated." });

            return Ok(curated.ToList());
        }

        private static double VendorAverageRating(VendorBriefV2Model vendor) =>
            vendor != null && vendor.TotalReviews > 0 ? (double)vendor.RatingSum / vendor.TotalReviews : 0;

        private async Task<List<ProductOverviewV2Model>> BuildProductOverviewsAsync()
        {
            var store = await storeContext.GetCurrentStoreAsync();

            var allProducts = await productService.SearchProductsAsync(
                storeId: store.Id,
                visibleIndividuallyOnly: true,
                showHidden: false);

            var vendors = await vendorService.GetAllVendorsAsync();
            var vendorsById = new Dictionary<int, VendorBriefV2Model>(vendors.Count);
            var popularityByVendor = new Dictionary<int, Dictionary<int, int>>(vendors.Count);
            foreach (var vendor in vendors)
            {
                vendorsById[vendor.Id] = await MapVendorAsync(vendor);
                var bestsellers = await orderReportService.BestSellersReportAsync(vendorId: vendor.Id, showHidden: true);
                popularityByVendor[vendor.Id] = bestsellers.ToDictionary(l => l.ProductId, l => l.TotalQuantity);
            }

            var categoryNameById = (await categoryService.GetAllCategoriesAsync(storeId: store.Id, showHidden: true))
                .ToDictionary(c => c.Id, c => c.Name);

            var ingredientOptionNames = await GetIngredientOptionNamesByIdAsync();

            // Found live on mysnacks-dev's real catalog: a couple of orphaned/test
            // products with no vendor assigned at all (VendorId not present in
            // vendorsById) - mobile's ProductOverviewApiModel.Vendor is non-optional
            // and every screen (vendor grouping, product-card vendor line) assumes it
            // exists, so these are dropped here rather than sent as Vendor: null.
            var products = allProducts.Where(p => vendorsById.ContainsKey(p.VendorId)).ToList();

            var result = new List<ProductOverviewV2Model>(products.Count);
            foreach (var batch in products.Chunk(ProductMapConcurrency))
            {
                var mapped = await Task.WhenAll(batch.Select(p =>
                    MapProductAsync(p, vendorsById, popularityByVendor, categoryNameById, ingredientOptionNames)));
                result.AddRange(mapped);
            }

            return result;
        }

        [HttpGet("vendors")]
        public async Task<IActionResult> GetVendors()
        {
            var vendors = await vendorService.GetAllVendorsAsync();

            var result = new List<VendorBriefV2Model>(vendors.Count);
            foreach (var vendor in vendors)
            {
                result.Add(await MapVendorAsync(vendor));
            }

            return Ok(result);
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var store = await storeContext.GetCurrentStoreAsync();
            var categories = await categoryService.GetAllCategoriesAsync(storeId: store.Id, showHidden: false);

            var result = new List<CategoryV2Model>(categories.Count);
            foreach (var category in categories)
            {
                // pageSize:1 just to read IPagedList.TotalCount cheaply, without
                // materializing every product in the category.
                var countPage = await productService.SearchProductsAsync(
                    pageSize: 1,
                    categoryIds: new List<int> { category.Id },
                    storeId: store.Id,
                    visibleIndividuallyOnly: true,
                    showHidden: false);

                var pictureUrl = category.PictureId > 0
                    ? await pictureService.GetPictureUrlAsync(category.PictureId, ThumbnailSize)
                    : null;

                result.Add(new CategoryV2Model
                {
                    Name = category.Name,
                    Count = countPage.TotalCount,
                    Description = category.Description,
                    PictureUrl = pictureUrl
                });
            }

            return Ok(result);
        }

        [HttpGet("ingredients")]
        public async Task<IActionResult> GetIngredients()
        {
            var ingredientsAttribute = await GetIngredientsAttributeAsync();
            if (ingredientsAttribute == null)
                return Ok(new List<IngredientV2Model>());

            var options = await specificationAttributeService
                .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(ingredientsAttribute.Id);

            var result = options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new IngredientV2Model { Name = o.Name, IsAllergen = o.IsAllergen })
                .ToList();

            return Ok(result);
        }

        [HttpGet("products/{id}/attributes")]
        public async Task<IActionResult> GetProductAttributes(int id)
        {
            var product = await productService.GetProductByIdAsync(id);
            if (product == null || product.Deleted)
                return NotFound();

            var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(id);

            var result = new List<ProductAttributeMappingV2Model>(mappings.Count);
            foreach (var mapping in mappings.OrderBy(m => m.DisplayOrder))
            {
                var attribute = await productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);

                var mappingModel = new ProductAttributeMappingV2Model
                {
                    MappingId = mapping.Id,
                    Name = attribute?.Name,
                    IsRequired = mapping.IsRequired,
                    ControlType = mapping.AttributeControlType.ToString()
                };

                if (mapping.ShouldHaveValues())
                {
                    var values = await productAttributeService.GetProductAttributeValuesAsync(mapping.Id);
                    foreach (var value in values.OrderBy(v => v.DisplayOrder))
                    {
                        mappingModel.Values.Add(new ProductAttributeValueV2Model
                        {
                            Id = value.Id,
                            Name = value.Name,
                            IsPreSelected = value.IsPreSelected,
                            PriceAdjustmentFormatted = await FormatAttributeAdjustmentAsync(value)
                        });
                    }
                }

                result.Add(mappingModel);
            }

            return Ok(result);
        }

        private async Task<string> FormatAttributeAdjustmentAsync(ProductAttributeValue value)
        {
            if (value.PriceAdjustment == 0)
                return null;

            var sign = value.PriceAdjustment > 0 ? "+" : "-";
            if (value.PriceAdjustmentUsePercentage)
                return $"{sign}{Math.Abs(value.PriceAdjustment).ToString("0.##", CultureInfo.InvariantCulture)}%";

            return $"{sign}{await priceFormatter.FormatPriceAsync(Math.Abs(value.PriceAdjustment))}";
        }

        private async Task<SpecificationAttribute> GetIngredientsAttributeAsync()
        {
            var attributes = await specificationAttributeService.GetSpecificationAttributesAsync();
            return attributes.FirstOrDefault(a => a.Name == IngredientsAttributeName);
        }

        // One upfront pass building {optionId -> name} for just the "Ingredients"
        // attribute's options, so per-product mapping is a dictionary lookup instead of
        // two extra sequential DB round trips (option-by-id, then attribute-by-id) per
        // mapped specification value.
        private async Task<Dictionary<int, string>> GetIngredientOptionNamesByIdAsync()
        {
            var ingredientsAttribute = await GetIngredientsAttributeAsync();
            if (ingredientsAttribute == null)
                return new Dictionary<int, string>();

            var options = await specificationAttributeService
                .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(ingredientsAttribute.Id);
            return options.ToDictionary(o => o.Id, o => o.Name);
        }

        private async Task<ProductOverviewV2Model> MapProductAsync(
            Product product,
            IReadOnlyDictionary<int, VendorBriefV2Model> vendorsById,
            IReadOnlyDictionary<int, Dictionary<int, int>> popularityByVendor,
            IReadOnlyDictionary<int, string> categoryNameById,
            IReadOnlyDictionary<int, string> ingredientOptionNames)
        {
            var pictures = await pictureService.GetPicturesByProductIdAsync(product.Id, 1);
            var imageUrl = pictures.Count > 0
                ? (await pictureService.GetPictureUrlAsync(pictures[0], ThumbnailSize)).Url
                : null;

            var productCategories = await categoryService.GetProductCategoriesByProductIdAsync(product.Id);
            var categoryName = productCategories.Count > 0 && categoryNameById.TryGetValue(productCategories[0].CategoryId, out var name)
                ? name
                : null;

            var specAttributes = await specificationAttributeService.GetProductSpecificationAttributesAsync(
                product.Id, showOnProductPage: true);
            var specificationLabels = specAttributes
                .Where(mapping => ingredientOptionNames.ContainsKey(mapping.SpecificationAttributeOptionId))
                .Select(mapping => ingredientOptionNames[mapping.SpecificationAttributeOptionId])
                .ToList();

            var attributeMappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id);

            vendorsById.TryGetValue(product.VendorId, out var vendorModel);
            var popularityCount = 0;
            if (popularityByVendor.TryGetValue(product.VendorId, out var popularityByProductId))
                popularityByProductId.TryGetValue(product.Id, out popularityCount);

            var priceFormatted = await priceFormatter.FormatPriceAsync(product.Price);

            return new ProductOverviewV2Model
            {
                Id = product.Id,
                Name = product.Name,
                PriceValue = product.Price,
                Price = priceFormatted,
                ImageUrl = imageUrl,
                CategoryName = categoryName,
                RibbonEnable = product.RibbonEnable,
                RibbonText = product.RibbonText,
                RatingSum = product.ApprovedRatingSum,
                TotalReviews = product.ApprovedTotalReviews,
                PopularityCount = popularityCount,
                Vendor = vendorModel,
                Description = PlainTextFromHtml(product.ShortDescription),
                SpecificationLabels = specificationLabels,
                HasAttributes = attributeMappings.Count > 0
            };
        }

        // Vendor.Description/Product.ShortDescription are admin-authored rich HTML
        // (TinyMCE) - mobile renders both as plain RN <Text>, which can't render
        // markup, so a raw "<p>Smoky grilled kebabs...</p>" would show up
        // literally on a vendor card. Nop.Core.Html.HtmlHelper.StripTags exists
        // but collapses HTML entities (&amp;, &nbsp;, ...) into a bare "@",
        // which would mangle real text (e.g. "Fish & Chips" -> "Fish @ Chips") -
        // this strips tags with a plain regex instead and properly HTML-decodes
        // entities via WebUtility, then collapses whitespace left behind by
        // adjacent block tags (e.g. "</p><p>").
        private static string PlainTextFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var noTags = Regex.Replace(html, "<[^>]*>", " ");
            var decoded = WebUtility.HtmlDecode(noTags);
            var collapsed = Regex.Replace(decoded, @"\s+", " ").Trim();
            return collapsed.Length > 0 ? collapsed : null;
        }

        private async Task<VendorBriefV2Model> MapVendorAsync(Nop.Core.Domain.Vendors.Vendor vendor)
        {
            var pictureUrl = vendor.PictureId > 0
                ? await pictureService.GetPictureUrlAsync(vendor.PictureId, ThumbnailSize)
                : null;

            var vendorProducts = await productService.SearchProductsAsync(vendorId: vendor.Id, showHidden: true);

            return new VendorBriefV2Model
            {
                Id = vendor.Id,
                Name = vendor.Name,
                PictureUrl = pictureUrl,
                RatingSum = vendorProducts.Sum(p => p.ApprovedRatingSum),
                TotalReviews = vendorProducts.Sum(p => p.ApprovedTotalReviews),
                Description = PlainTextFromHtml(vendor.Description)
            };
        }
    }
}
