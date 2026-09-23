using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
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
    /// against real data instead of mock fixtures. Revisit if the catalog outgrows "small
    /// enough to fetch once" - api/catalog/product-search already exists for real
    /// server-side paginated search if that's ever needed.
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
        IStoreContext storeContext)
        : BaseApiController
    {
        private const string IngredientsAttributeName = "Ingredients";
        private const int ThumbnailSize = 300;

        // Per-request memoization only (controllers are instantiated per-request) - not a
        // shared IStaticCacheManager entry, deliberately: CatalogApiController already caches
        // this same aggregate under NopModelCacheDefaults.ApiVendorRatingKey with its own
        // private VendorRatingAggregate type, and a static cache is not guaranteed to be
        // safe to share across two different CLR types under one string key (risks a cast
        // failure depending on the cache manager's implementation). Recomputing per request
        // is cheap for this catalog's size; this dictionary just avoids redoing it once per
        // product within a single GetProducts() call for products sharing a vendor.
        private readonly Dictionary<int, VendorRatingAggregate> _vendorRatingMemo = new();
        // Same per-request-only reasoning as _vendorRatingMemo - keyed by vendorId since
        // BestSellersReportAsync(vendorId:) returns one report per vendor, not per product.
        private readonly Dictionary<int, Dictionary<int, int>> _popularityByVendorMemo = new();

        public class ProductOverviewV2Model
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal PriceValue { get; set; }
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
        }

        public class IngredientV2Model
        {
            public string Name { get; set; }
            public bool IsAllergen { get; set; }
        }

        private class VendorRatingAggregate
        {
            public int RatingSum { get; set; }
            public int TotalReviews { get; set; }
        }

        [HttpGet("products")]
        public async Task<IActionResult> GetProducts()
        {
            var store = await storeContext.GetCurrentStoreAsync();

            var products = await productService.SearchProductsAsync(
                storeId: store.Id,
                visibleIndividuallyOnly: true,
                showHidden: false);

            var result = new List<ProductOverviewV2Model>(products.Count);
            foreach (var product in products)
            {
                result.Add(await MapProductAsync(product));
            }

            return Ok(result);
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

                result.Add(new CategoryV2Model
                {
                    Name = category.Name,
                    Count = countPage.TotalCount,
                    Description = category.Description
                });
            }

            return Ok(result);
        }

        [HttpGet("ingredients")]
        public async Task<IActionResult> GetIngredients()
        {
            var attributes = await specificationAttributeService.GetSpecificationAttributesAsync();
            var ingredientsAttribute = attributes.FirstOrDefault(a => a.Name == IngredientsAttributeName);
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

        private async Task<ProductOverviewV2Model> MapProductAsync(Product product)
        {
            var pictures = await pictureService.GetPicturesByProductIdAsync(product.Id, 1);
            var imageUrl = pictures.Count > 0
                ? await pictureService.GetPictureUrlAsync(pictures[0].Id, ThumbnailSize)
                : null;

            var productCategories = await categoryService.GetProductCategoriesByProductIdAsync(product.Id);
            string categoryName = null;
            if (productCategories.Count > 0)
            {
                var category = await categoryService.GetCategoryByIdAsync(productCategories[0].CategoryId);
                categoryName = category?.Name;
            }

            var specAttributes = await specificationAttributeService.GetProductSpecificationAttributesAsync(
                product.Id, showOnProductPage: true);
            var specificationLabels = new List<string>();
            foreach (var mapping in specAttributes)
            {
                var option = await specificationAttributeService
                    .GetSpecificationAttributeOptionByIdAsync(mapping.SpecificationAttributeOptionId);
                if (option == null)
                    continue;

                var attribute = await specificationAttributeService.GetSpecificationAttributeByIdAsync(option.SpecificationAttributeId);
                if (attribute?.Name == IngredientsAttributeName)
                    specificationLabels.Add(option.Name);
            }

            VendorBriefV2Model vendorModel = null;
            if (product.VendorId > 0)
            {
                var vendor = await vendorService.GetVendorByIdAsync(product.VendorId);
                if (vendor != null)
                    vendorModel = await MapVendorAsync(vendor);
            }

            if (!_popularityByVendorMemo.TryGetValue(product.VendorId, out var popularityByProductId))
            {
                var bestsellers = await orderReportService.BestSellersReportAsync(vendorId: product.VendorId, showHidden: true);
                popularityByProductId = bestsellers.ToDictionary(l => l.ProductId, l => l.TotalQuantity);
                _popularityByVendorMemo[product.VendorId] = popularityByProductId;
            }
            popularityByProductId.TryGetValue(product.Id, out var popularityCount);

            return new ProductOverviewV2Model
            {
                Id = product.Id,
                Name = product.Name,
                PriceValue = product.Price,
                ImageUrl = imageUrl,
                CategoryName = categoryName,
                RibbonEnable = product.RibbonEnable,
                RibbonText = product.RibbonText,
                RatingSum = product.ApprovedRatingSum,
                TotalReviews = product.ApprovedTotalReviews,
                PopularityCount = popularityCount,
                Vendor = vendorModel,
                Description = product.ShortDescription,
                SpecificationLabels = specificationLabels
            };
        }

        private async Task<VendorBriefV2Model> MapVendorAsync(Vendor vendor)
        {
            var pictureUrl = vendor.PictureId > 0
                ? await pictureService.GetPictureUrlAsync(vendor.PictureId, ThumbnailSize)
                : null;

            if (!_vendorRatingMemo.TryGetValue(vendor.Id, out var vendorRating))
            {
                var vendorProducts = await productService.SearchProductsAsync(vendorId: vendor.Id, showHidden: true);
                vendorRating = new VendorRatingAggregate
                {
                    RatingSum = vendorProducts.Sum(p => p.ApprovedRatingSum),
                    TotalReviews = vendorProducts.Sum(p => p.ApprovedTotalReviews)
                };
                _vendorRatingMemo[vendor.Id] = vendorRating;
            }

            return new VendorBriefV2Model
            {
                Id = vendor.Id,
                Name = vendor.Name,
                PictureUrl = pictureUrl,
                RatingSum = vendorRating.RatingSum,
                TotalReviews = vendorRating.TotalReviews,
                Description = vendor.Description
            };
        }
    }
}
