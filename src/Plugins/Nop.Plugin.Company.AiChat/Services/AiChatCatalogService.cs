using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Media;
using Nop.Services.Vendors;

namespace Nop.Plugin.Company.AiChat.Services
{
    public partial class AiChatCatalogService : IAiChatCatalogService
    {
        private const string IngredientsAttributeName = "Ingredients";
        private const int ThumbnailSize = 300;

        // Mirrors Nop.Plugin.Company.Company's CustomerPreferencesService.ALLERGIES_KEY exactly
        // (same generic attribute, same JSON-array-of-strings shape) so this plugin can read the
        // customer's saved allergies without taking a hard project reference on that plugin -
        // no cross-plugin ProjectReference exists anywhere in this codebase, this keeps it that way.
        private const string AllergiesGenericAttributeKey = "ALLERGIES_KEY";

        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IVendorService _vendorService;
        private readonly ISpecificationAttributeService _specificationAttributeService;
        private readonly IPictureService _pictureService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IGenericAttributeService _genericAttributeService;

        public AiChatCatalogService(
            IProductService productService,
            ICategoryService categoryService,
            IVendorService vendorService,
            ISpecificationAttributeService specificationAttributeService,
            IPictureService pictureService,
            IPriceFormatter priceFormatter,
            IGenericAttributeService genericAttributeService)
        {
            _productService = productService;
            _categoryService = categoryService;
            _vendorService = vendorService;
            _specificationAttributeService = specificationAttributeService;
            _pictureService = pictureService;
            _priceFormatter = priceFormatter;
            _genericAttributeService = genericAttributeService;
        }

        public virtual async Task<IList<AiChatProductCandidate>> SearchAsync(Customer customer, int storeId, string query, int maxResults = 6)
        {
            var allergies = await GetCustomerAllergiesAsync(customer, storeId);

            var page = await _productService.SearchProductsAsync(
                pageSize: maxResults * 8, // headroom - allergy filtering happens after the query
                storeId: storeId,
                visibleIndividuallyOnly: true,
                showHidden: false,
                keywords: string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                searchDescriptions: true);

            var ingredientOptionNames = await GetIngredientOptionNamesByIdAsync();
            var vendorNameById = new Dictionary<int, string>();
            var categoryNameById = new Dictionary<int, string>();

            var results = new List<AiChatProductCandidate>();
            foreach (var product in page)
            {
                var specAttributes = await _specificationAttributeService.GetProductSpecificationAttributesAsync(
                    product.Id, showOnProductPage: true);
                var ingredientLabels = specAttributes
                    .Where(mapping => ingredientOptionNames.ContainsKey(mapping.SpecificationAttributeOptionId))
                    .Select(mapping => ingredientOptionNames[mapping.SpecificationAttributeOptionId])
                    .ToList();

                if (allergies.Count > 0 && ingredientLabels.Any(label => allergies.Contains(label, System.StringComparer.OrdinalIgnoreCase)))
                    continue;

                if (!vendorNameById.TryGetValue(product.VendorId, out var vendorName))
                {
                    var vendor = await _vendorService.GetVendorByIdAsync(product.VendorId);
                    vendorName = vendor?.Name;
                    vendorNameById[product.VendorId] = vendorName;
                }

                var productCategories = await _categoryService.GetProductCategoriesByProductIdAsync(product.Id);
                string categoryName = null;
                if (productCategories.Count > 0)
                {
                    var categoryId = productCategories[0].CategoryId;
                    if (!categoryNameById.TryGetValue(categoryId, out categoryName))
                    {
                        var category = await _categoryService.GetCategoryByIdAsync(categoryId);
                        categoryName = category?.Name;
                        categoryNameById[categoryId] = categoryName;
                    }
                }

                var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id, 1);
                var imageUrl = pictures.Count > 0
                    ? (await _pictureService.GetPictureUrlAsync(pictures[0], ThumbnailSize)).Url
                    : null;

                results.Add(new AiChatProductCandidate
                {
                    Id = product.Id,
                    Name = product.Name,
                    PriceValue = product.Price,
                    Price = await _priceFormatter.FormatPriceAsync(product.Price),
                    ImageUrl = imageUrl,
                    VendorName = vendorName,
                    CategoryName = categoryName,
                    IngredientLabels = ingredientLabels
                });

                if (results.Count >= maxResults)
                    break;
            }

            return results;
        }

        public virtual async Task<IList<string>> GetCustomerAllergiesAsync(Customer customer, int storeId)
        {
            var json = await _genericAttributeService.GetAttributeAsync<string>(customer, AllergiesGenericAttributeKey, storeId);
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        private async Task<SpecificationAttribute> GetIngredientsAttributeAsync()
        {
            var attributes = await _specificationAttributeService.GetSpecificationAttributesAsync();
            return attributes.FirstOrDefault(a => a.Name == IngredientsAttributeName);
        }

        private async Task<Dictionary<int, string>> GetIngredientOptionNamesByIdAsync()
        {
            var ingredientsAttribute = await GetIngredientsAttributeAsync();
            if (ingredientsAttribute == null)
                return new Dictionary<int, string>();

            var options = await _specificationAttributeService
                .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(ingredientsAttribute.Id);
            return options.ToDictionary(o => o.Id, o => o.Name);
        }
    }
}
