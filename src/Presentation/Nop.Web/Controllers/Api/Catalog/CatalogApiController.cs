using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Discounts;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Seo;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Infrastructure.Cache;
using Nop.Web.Models.Api.Catalog;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.Common;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Security;

namespace Nop.Web.Controllers.Api.Security
{
    [Produces("application/json")]
    [Route("api/catalog")]
    [AuthorizeAttribute]
    public partial class CatalogApiController : BaseApiController
    {
        #region Fields

        private readonly LocalizationSettings _localizationSettings;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly IOrderService _orderService;
        private readonly IOrderReportService _orderReportService;
        private readonly ISpecificationAttributeService _specificationAttributeService;
        private readonly IProductModelFactory _productModelFactory;
        private readonly IProductService _productService;
        private readonly ILocalizationService _localizationService;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly CatalogSettings _catalogSettings;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;
        private readonly ICategoryService _categoryService;
        private readonly ICatalogModelFactory _catalogModelFactory;
        private readonly ITopicModelFactory _topicModelFactory;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IVendorService _vendorService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ICustomerService _customerService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly IStaticCacheManager _staticCacheManager;
        private readonly ISettingService _settingService;
        private readonly IProductAttributeService _productAttributeService;
        private readonly ICompanyService _companyService;
        private readonly IDiscountService _discountService;
        private readonly IPermissionService _permissionService;
        private readonly IGenericAttributeService _genericAttributeService;
        private const string LastUnpublishedProductIdsKey = "LastUnpublishedProductIds"; 

        private static readonly AttributeControlType[] _allowedAttributeControlTypes = new[] {
            AttributeControlType.DropdownList,
            AttributeControlType.RadioList,
            AttributeControlType.Checkboxes,
        };

        #endregion

        #region Ctor

        public CatalogApiController(
            LocalizationSettings localizationSettings,
            IWorkflowMessageService workflowMessageService,
            IOrderService orderService,
            IOrderReportService orderReportService,
            ISpecificationAttributeService specificationAttributeService,
            IUrlRecordService urlRecordService,
            ICatalogModelFactory catalogModelService,
            IProductModelFactory productModelFactory,
            ITopicModelFactory topicModelFactory,
            ICategoryService categoryService,
            IProductService productService,
            ILocalizationService localizationService,
            ICustomerActivityService customerActivityService,
            CatalogSettings catalogSettings,
            IStoreContext storeContext,
            IWorkContext workContext,
            IEventPublisher eventPublisher,
            IVendorService vendorService,
            ICustomerService customerService,
            IDateTimeHelper dateTimeHelper,
            IStaticCacheManager staticCacheManager,
            ISettingService settingService,
            IProductAttributeService productAttributeService,
            IProductAvailabilityService productAvailabilityService,
            ICompanyService companyService,
            IDiscountService discountService, 
            IPermissionService permissionService, 
            IGenericAttributeService genericAttributeService)
        {
            _localizationSettings = localizationSettings;
            _workflowMessageService = workflowMessageService;
            _orderReportService = orderReportService;
            _orderService = orderService;
            _specificationAttributeService = specificationAttributeService;
            _urlRecordService = urlRecordService;
            _categoryService = categoryService;
            _catalogModelFactory = catalogModelService;
            _topicModelFactory = topicModelFactory;
            _productService = productService;
            _localizationService = localizationService;
            _customerActivityService = customerActivityService;
            _catalogSettings = catalogSettings;
            _storeContext = storeContext;
            _productModelFactory = productModelFactory;
            _workContext = workContext;
            _vendorService = vendorService;
            _eventPublisher = eventPublisher;
            _customerService = customerService;
            _dateTimeHelper = dateTimeHelper;
            _staticCacheManager = staticCacheManager;
            _settingService = settingService;
            _productAttributeService = productAttributeService;
            _companyService = companyService;
            _discountService = discountService;
            _permissionService = permissionService;
            _genericAttributeService = genericAttributeService;
        }

        #endregion

        #region Utility

        [NonAction]
        private async Task<ProductSpecificationApiModel> PrepareProductSpecificationAttributeModelAsync(Product product)
        {
            var result = new ProductSpecificationApiModel();
            if (product == null)
            {
                var allProductSpecifications = new ProductSpecificationApiModel();
                var specificationCacheKey = _staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.AllProductSpecificationsModelKey, product, await _storeContext.GetCurrentStoreAsync());

                allProductSpecifications = await _staticCacheManager.GetAsync(specificationCacheKey, async () =>
                {
                    var productAllSpecificationAttributes = await _specificationAttributeService.GetProductSpecificationAttributesAsync();
                    foreach (var psa in productAllSpecificationAttributes)
                    {
                        var singleOption = await _specificationAttributeService.GetSpecificationAttributeOptionByIdAsync(psa.SpecificationAttributeOptionId);
                        var checkModel = result.ProductSpecificationAttribute.FirstOrDefault(model => model.Id == singleOption.SpecificationAttributeId || model.Name == singleOption.Name);
                        if (checkModel == null)
                        {
                            var model1 = new ProductSpecificationAttributeApiModel();
                            var attribute = await _specificationAttributeService.GetSpecificationAttributeByIdAsync(singleOption.SpecificationAttributeId);
                            model1.Id = attribute.Id;
                            model1.Name = await _localizationService.GetLocalizedAsync(attribute, x => x.Name);
                            var options = await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attribute.Id);
                            foreach (var option in options)
                            {
                                model1.Values.Add(new ProductSpecificationAttributeValueApiModel
                                {
                                    AttributeTypeId = psa.AttributeTypeId,
                                    ColorSquaresRgb = option.ColorSquaresRgb,
                                    ValueRaw = psa.AttributeType switch
                                    {
                                        SpecificationAttributeType.Option => WebUtility.HtmlEncode(await _localizationService.GetLocalizedAsync(option, x => x.Name)),
                                        SpecificationAttributeType.CustomText => WebUtility.HtmlEncode(await _localizationService.GetLocalizedAsync(psa, x => x.CustomValue)),
                                        SpecificationAttributeType.CustomHtmlText => await _localizationService.GetLocalizedAsync(psa, x => x.CustomValue),
                                        SpecificationAttributeType.Hyperlink => $"<a href='{psa.CustomValue}' target='_blank'>{psa.CustomValue}</a>",
                                        _ => null
                                    }
                                });
                            }
                            result.ProductSpecificationAttribute.Add(model1);
                        }
                    }
                    var allVendors = await _vendorService.GetAllVendorsAsync();
                    result.ProductVendors = allVendors.Select(x => new VendorBriefInfoModel
                    {
                        Id = x.Id,
                        Name = x.Name,
                        SeName = _urlRecordService.GetSeNameAsync(x).GetAwaiter().GetResult()
                    }).ToList();
                    return result;
                });

                return allProductSpecifications;
            }

            var productSpecifications = new ProductSpecificationApiModel();
            var cacheKey = _staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.ProductSpecificationsModelKey, product, await _storeContext.GetCurrentStoreAsync());

            productSpecifications = await _staticCacheManager.GetAsync(cacheKey, async () =>
            {
                var productSpecificationAttributes = await _specificationAttributeService.GetProductSpecificationAttributesAsync(
                product.Id, showOnProductPage: true);

                foreach (var psa in productSpecificationAttributes)
                {
                    var option = await _specificationAttributeService.GetSpecificationAttributeOptionByIdAsync(psa.SpecificationAttributeOptionId);
                    var checkModel = result.ProductSpecificationAttribute.FirstOrDefault(model => model.Id == option.SpecificationAttributeId);
                    var attribute = await _specificationAttributeService.GetSpecificationAttributeByIdAsync(option.SpecificationAttributeId);
                    var attributeName = await _localizationService.GetLocalizedAsync(attribute, x => x.Name);
                    if (checkModel == null)
                    {
                        var model1 = new ProductSpecificationAttributeApiModel();
                        model1.Id = attribute.Id;
                        model1.Name = await _localizationService.GetLocalizedAsync(attribute, x => x.Name);
                        model1.Values.Add(new ProductSpecificationAttributeValueApiModel
                        {
                            AttributeTypeId = psa.AttributeTypeId,
                            ColorSquaresRgb = option.ColorSquaresRgb,
                            ValueRaw = psa.AttributeType switch
                            {
                                SpecificationAttributeType.Option => WebUtility.HtmlEncode(await _localizationService.GetLocalizedAsync(option, x => x.Name)),
                                SpecificationAttributeType.CustomText => WebUtility.HtmlEncode(await _localizationService.GetLocalizedAsync(psa, x => x.CustomValue)),
                                SpecificationAttributeType.CustomHtmlText => await _localizationService.GetLocalizedAsync(psa, x => x.CustomValue),
                                SpecificationAttributeType.Hyperlink => $"<a href='{psa.CustomValue}' target='_blank'>{psa.CustomValue}</a>",
                                _ => null
                            }
                        });
                        result.ProductSpecificationAttribute.Add(model1);
                    }
                    else if (result.ProductSpecificationAttribute.Select(x => x.Name == attributeName).Any())
                    {
                        var addAttributeModel = result.ProductSpecificationAttribute.Where(x => x.Name == attributeName).FirstOrDefault();
                        addAttributeModel.Values.Add(new ProductSpecificationAttributeValueApiModel
                        {
                            AttributeTypeId = psa.AttributeTypeId,
                            ColorSquaresRgb = option.ColorSquaresRgb,
                            ValueRaw = psa.AttributeType switch
                            {
                                SpecificationAttributeType.Option => WebUtility.HtmlEncode(await _localizationService.GetLocalizedAsync(option, x => x.Name)),
                                SpecificationAttributeType.CustomText => WebUtility.HtmlEncode(await _localizationService.GetLocalizedAsync(psa, x => x.CustomValue)),
                                SpecificationAttributeType.CustomHtmlText => await _localizationService.GetLocalizedAsync(psa, x => x.CustomValue),
                                SpecificationAttributeType.Hyperlink => $"<a href='{psa.CustomValue}' target='_blank'>{psa.CustomValue}</a>",
                                _ => null
                            }
                        });
                    }
                }
                var vendor = await _vendorService.GetVendorByProductIdAsync(product.Id);
                if (vendor != null)
                {
                    result.ProductVendors.Add(new VendorBriefInfoModel
                    {
                        Id = vendor.Id,
                        Name = vendor.Name,
                        SeName = _urlRecordService.GetSeNameAsync(vendor).GetAwaiter().GetResult()
                    });
                }
                return result;
            });
            return productSpecifications;
        }

        [NonAction]
        private async Task<ProductAttributesApiModel> PrepareProductAttributesApiModel(Product product)
        {
            var allowedProductAttributeMappings =
                (await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id))
                .Where(pam =>
                    _allowedAttributeControlTypes.Contains(pam.AttributeControlType))
                .ToArray();

            var productAttributes =
                (await _productAttributeService.GetProductAttributeByIdsAsync(
                allowedProductAttributeMappings
                    .Select(pam => pam.ProductAttributeId)
                    .ToArray()));

            var pamJoinedWithAttributes =
                allowedProductAttributeMappings.GroupJoin(
                productAttributes,
                pamKey => pamKey.ProductAttributeId,
                paKey => paKey.Id,
                (pam, pas) =>
                {
                    return pas.Select(async pa =>
                    {
                        var pavs =
                            await _productAttributeService.GetProductAttributeValuesAsync(pam.Id);
                        return new ProductAttributeApiModel
                        {
                            Id = pa.Id,
                            Name = pa.Name,
                            IsRequired = pam.IsRequired,
                            AttributeControlType = pam.AttributeControlType,
                            AttributeValues = pavs.Select(pav => new ProductAttributeValueApiModel()
                            {
                                Id = pav.Id,
                                IsPreSelected = pav.IsPreSelected,
                                PriceAdjustment = pav.PriceAdjustment,
                                PriceAdjustmentUsePercentage = pav.PriceAdjustmentUsePercentage,
                                Name = pav.Name
                            }).ToArray()
                        };
                    });
                }).ToArray();

            var result = await Task.WhenAll(pamJoinedWithAttributes
                .SelectMany(pam => pam));

            return new ProductAttributesApiModel
            {
                ProductAttributes = result
            };
        }

        [NonAction]
        protected virtual async Task<IEnumerable<ProductOverviewApiModel>> PrepareApiProductOverviewModels(
            IEnumerable<Product> products)
        {
            if (products == null)
                throw new ArgumentNullException(nameof(products));

            var models = new List<ProductOverviewApiModel>();

            foreach (var product in products)
            {
                var productDetailsModel = await _productModelFactory.PrepareProductDetailsModelAsync(product);

                var popularityByVendor = (await _staticCacheManager.GetAsync(
                    _staticCacheManager.PrepareKeyForDefaultCache(
                        NopModelCacheDefaults.ApiBestsellersVendorIdsKey, product.VendorId),
                    async () => (await _orderReportService.BestSellersReportAsync(
                        showHidden: true,
                        vendorId: product.VendorId)).ToImmutableDictionary(k => k.ProductId)));

                var productOverviewApiModel = new ProductOverviewApiModel()
                {
                    Id = product.Id,
                    Name = productDetailsModel.Name,
                    ShortDescription = productDetailsModel.ShortDescription,
                    FullDescription = productDetailsModel.FullDescription,
                    SeName = productDetailsModel.SeName,
                    CategoryName = string.Join(',',
                        productDetailsModel.Breadcrumb.CategoryBreadcrumb.Select(b => b.Name)),
                    Price = productDetailsModel.ProductPrice.Price,
                    PriceValue = productDetailsModel.ProductPrice.PriceValue,
                    RatingSum = productDetailsModel.ProductReviewOverview.RatingSum,
                    TotalReviews = productDetailsModel.ProductReviewOverview.TotalReviews,
                    PopularityCount =
                        popularityByVendor.TryGetValue(product.Id, out var productPopularity)
                            ? productPopularity.TotalQuantity
                            : 0,
                    ImageUrl = productDetailsModel.DefaultPictureModel.ImageUrl, // TODO: add all images
                    RibbonEnable = product.RibbonEnable,
                    RibbonText = product.RibbonText,
                    Vendor = productDetailsModel.VendorModel,
                    ProductSpecificationModel = await PrepareProductSpecificationAttributeModelAsync(product),
                    ProductAttributesModel = await PrepareProductAttributesApiModel(product)
                };

                if (product.HasDiscountsApplied)
                {
                    var appliedDiscounts = await _discountService.GetAppliedDiscountsAsync(product);

                    _discountService.GetPreferredDiscount(
                        appliedDiscounts.Where(d => (d.StartDateUtc <= DateTime.UtcNow && d.EndDateUtc > DateTime.UtcNow) || (d.StartDateUtc == null && d.EndDateUtc == null)).ToList(),
                        product.Price,
                        out var discountAmount);

                    if (discountAmount != 0)
                    {
                        productOverviewApiModel.RibbonText = $"-{(int)((discountAmount / product.Price) * 100)}%";
                        productOverviewApiModel.RibbonEnable = true;
                        productOverviewApiModel.DiscountAmount = (int)((discountAmount / product.Price) * 100);
                    }
                }

                models.Add(productOverviewApiModel);
            }

            return models;
        }

        [NonAction]
        protected virtual async Task<CustomerProductReviewsModel> PrepareCustomerProductReviewsModel(int? page, int customerId)
        {
            var pageSize = _catalogSettings.ProductReviewsPageSizeOnAccountPage;
            var pageIndex = 0;

            if (page > 0)
            {
                pageIndex = page.Value - 1;
            }

            var list = await _productService.GetAllProductReviewsAsync(customerId: customerId,
                approved: null,
                pageIndex: pageIndex,
                pageSize: pageSize);

            var productReviews = new List<CustomerProductReviewModel>();

            foreach (var review in list)
            {
                var product = await _productService.GetProductByIdAsync(review.ProductId);
                var dateTime = await _dateTimeHelper.ConvertToUserTimeAsync(product.CreatedOnUtc, DateTimeKind.Utc);
                var productReviewModel = new CustomerProductReviewModel
                {
                    Title = review.Title,
                    ProductId = product.Id,
                    ProductName = await _localizationService.GetLocalizedAsync(product, p => p.Name),
                    ProductSeName = await _urlRecordService.GetSeNameAsync(product),
                    Rating = review.Rating,
                    ReviewText = review.ReviewText,
                    ReplyText = review.ReplyText,
                    WrittenOnStr = dateTime.ToString("g")
                };

                if (_catalogSettings.ProductReviewsMustBeApproved)
                {
                    productReviewModel.ApprovalStatus = review.IsApproved
                        ? await _localizationService.GetResourceAsync("Account.CustomerProductReviews.ApprovalStatus.Approved")
                        : await _localizationService.GetResourceAsync("Account.CustomerProductReviews.ApprovalStatus.Pending");
                }
                productReviews.Add(productReviewModel);
            }

            var pagerModel = new PagerModel
            {
                PageSize = list.PageSize,
                TotalRecords = list.TotalCount,
                PageIndex = list.PageIndex,
                ShowTotalSummary = false,
                RouteActionName = "CustomerProductReviewsPaged",
                UseRouteLinks = true,
                RouteValues = new CustomerProductReviewsModel.CustomerProductReviewsRouteValues { pageNumber = pageIndex }
            };

            var model = new CustomerProductReviewsModel
            {
                ProductReviews = productReviews,
                PagerModel = pagerModel
            };

            return model;
        }

        #endregion

        #region Category

        [HttpGet("category-list")]
        public async Task<IActionResult> GetAllCategories()
        {
            var categories = await _catalogModelFactory.PrepareCategorySimpleModelsAsync(filterVendorCategories: true);
            if (categories.Count > 0)
            {
                var result = categories.Select(cat => new
                {
                    id = cat.Id,
                    name = cat.Name,
                    numberOfProducts = cat.NumberOfProducts,
                    numberOfSubCategories = cat.SubCategories.Count,
                    seName = cat.SeName,
                    pictureUrl = cat.PictureUrl
                });
                return Ok(result);
            }
            return Ok(new { message = await _localizationService.GetResourceAsync("Category.Not.Found") });
        }

        #endregion

        #region Product

        [HttpGet("product-specification-attributes-and-productvendors")]
        public async Task<IActionResult> AllProductSpecificationAndProductVendors()
        {
            //model
            var model = await PrepareProductSpecificationAttributeModelAsync(null);
            return Ok(model);
        }

        public class ProductPublishUnpublishModel
        {
            public string[] VendorsWhitelist { get; set; }
            public int CompanyId { get; set; }
            public bool DoNotUseLastUnpublishedProductList { get; set; }
        }
        
        [HttpPost("product-publish-whitelist")]
        public async Task<IActionResult> ProductPublishWhitelist([FromBody] ProductPublishUnpublishModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageProducts) ||
                (await _workContext.GetCurrentVendorAsync()) != null)
                return Unauthorized(new {success = false, message = "Not enough permissions"});

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            var company = await _companyService.GetCompanyByIdAsync(model.CompanyId);
            if (company == null)
                return BadRequest(new {success = false, message = "Company not found"});
            
            var companyAllowedVendorIds =
                (await _companyService.GetCompanyVendorsByCompanyAsync(model.CompanyId))
                .Select(x => x.VendorId)
                .ToHashSet();
            
            var companyAllVendors =
                (await _vendorService.GetAllVendorsAsync(showHidden: true))
                .Where(x => companyAllowedVendorIds.Contains(x.Id))
                .ToArray();
            
            var vendorsToUnpublish = companyAllVendors
                .Where(x => !model.VendorsWhitelist.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var vendorsToPublish = companyAllVendors.Except(vendorsToUnpublish);
            var vendorsNotFound = model.VendorsWhitelist
                .Where(x => !companyAllVendors.Any(y => string.Equals(y.Name, x, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (model.DoNotUseLastUnpublishedProductList)
                return BadRequest(new { success = false, message = "Not implemented" });

            var lastUnpublishedProductIds =
                (await _genericAttributeService.GetAttributeAsync(company, LastUnpublishedProductIdsKey, storeId,
                    ""))
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToHashSet();
            
            // Unpublish products and save their IDs for future publishing back
            var unpublishedProductIdsByVendor = new Dictionary<int, List<int>>();
            foreach (var vendor in vendorsToUnpublish)
            {
                var unpublishedProductIds = new List<int>();
                
                var publishedProducts = 
                    await _productService.SearchProductsAsync(storeId: storeId, vendorId: vendor.Id, showHidden: false);
                foreach (var product in publishedProducts)
                {
                    product.Published = false;
                    await _productService.UpdateProductAsync(product);
                    unpublishedProductIds.Add(product.Id);
                }
                unpublishedProductIdsByVendor[vendor.Id] = unpublishedProductIds;
            }
            // Save unpublished product IDs
            await _genericAttributeService.SaveAttributeAsync(company, LastUnpublishedProductIdsKey, 
                string.Join(",", lastUnpublishedProductIds.Union(unpublishedProductIdsByVendor.Values.SelectMany(x => x))), 
                storeId);
            
            // Publish products
            var publishedProductIdsByVendor = new Dictionary<int, List<int>>();
            foreach (var vendor in vendorsToPublish)
            {
                var publishedProductIds = new List<int>();
                
                var unpublishedProducts = 
                    await _productService.SearchProductsAsync(storeId: storeId, 
                        vendorId: vendor.Id, 
                        showHidden: true, 
                        overridePublished: false);
                
                foreach (var product in unpublishedProducts)
                {
                    // Publish only if we ever unpublished those products by ourselves
                    if(lastUnpublishedProductIds.Contains(product.Id))
                    {
                        product.Published = true;
                        await _productService.UpdateProductAsync(product);
                        publishedProductIds.Add(product.Id);
                    }
                }
                publishedProductIdsByVendor[vendor.Id] = publishedProductIds;
            }
            
            return Ok(new
            {
                success = true,
                notFoundVendors = vendorsNotFound,
                publishedProductCountByVendor = publishedProductIdsByVendor
                    .ToDictionary(x => companyAllVendors.First(v => v.Id == x.Key).Name, x => x.Value.Count),
                unpublishedProductCountByVendor = unpublishedProductIdsByVendor
                    .ToDictionary(x => companyAllVendors.First(v => v.Id == x.Key).Name, x => x.Value.Count),
            });
        }
        
        [HttpGet("product-search")]
        public async Task<IActionResult> SearchProducts(SearchProductByFilters searchModel)
        {
            var categoryIds =
                searchModel.CategoryId.HasValue ? new List<int> { searchModel.CategoryId.Value }
                    :
                (await _categoryService.GetAllCategoriesAsync())
                .Select(c => c.Id)
                .Where(id => id != 0)
                .ToList();

            var products = (await _productService.SearchProductsAsync(
                pageIndex: searchModel.Page ?? 0,
                pageSize: searchModel.PageSize ?? int.MaxValue,
                keywords: searchModel.Keyword,
                showHidden: false,
                categoryIds: categoryIds,
                searchCustomerVendors: true,
                vendorId: searchModel.VendorId ?? 0,
                onlyDiscounted: searchModel.BestDeals,
                orderBy: searchModel.PriceLow == true ? ProductSortingEnum.PriceAsc : searchModel.PriceHigh == true ? ProductSortingEnum.PriceDesc : ProductSortingEnum.Position));

            if (!products.Any())
            {
                return Ok(
                    Enumerable.Empty<ProductOverviewApiModel>()
                );
            }

            //model
            var model = await PrepareApiProductOverviewModels(products);

            if (searchModel.Popular == true)
            {
                model = model.OrderByDescending(p => p.PopularityCount);
            }
            else if (searchModel.TopRated == true)
            {
                model = model.OrderByDescending(p => p.RatingSum / p.TotalReviews);
            }
            else if (searchModel.BestDeals == true)
            {
                model = model.Where(p => p.RibbonEnable == true);
            }
            return Ok(model);
        }

        [HttpGet("product/{productId}")]
        public async Task<IActionResult> ProductById(int productId)
        {
            var product = await _productService.GetProductByIdAsync(productId);

            if (product == null)
            {
                return NotFound(new { success = false, message = $"Product with id {productId} not found" });
            }

            var model = await PrepareApiProductOverviewModels(new[] { product });

            return Ok(model.First());
        }

        #endregion

        #region Product Review


        [HttpPost("add-product-reviews")]
        public virtual async Task<IActionResult> ProductReviewsAdd([FromBody] AddProductReviewApiModel model)
        {
            var product = await _productService.GetProductByIdAsync(model.Id);
            var curCus = await _workContext.GetCurrentCustomerAsync();
            if (product == null || product.Deleted ||
                !product.AllowCustomerReviews) // TODO: associate review with existing order 
            {
                return Ok(new
                {
                    success = false,
                    message = await _localizationService.GetResourceAsync("Product.Not.Found")
                });
            }

            if (await _customerService.IsGuestAsync(curCus) && !_catalogSettings.AllowAnonymousUsersToReviewProduct)
            {
                return Ok(new
                {
                    success = false,
                    message = await _localizationService.GetResourceAsync("Reviews.OnlyRegisteredUsersCanWriteReviews")
                });
            }

            if (_catalogSettings.ProductReviewPossibleOnlyAfterPurchasing)
            {
                var hasCompletedOrders = await _orderService.SearchOrdersAsync(customerId: curCus.Id,
                    productId: model.Id,
                    osIds: new List<int> { (int)OrderStatus.Complete },
                    pageSize: 1);

                if (!hasCompletedOrders.Any())
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizationService.GetResourceAsync(
                            "Reviews.ProductReviewPossibleOnlyAfterPurchasing")
                    });
                }
            }

            if (ModelState.IsValid)
            {
                //save review
                var rating = model.Rating;
                if (rating < 1 || rating > 5)
                    rating = _catalogSettings.DefaultProductRatingValue;
                var isApproved = !_catalogSettings.ProductReviewsMustBeApproved;

                var productReview = new ProductReview
                {
                    ProductId = product.Id,
                    CustomerId = curCus.Id,
                    Title = model.Title,
                    ReviewText = model.ReviewText,
                    Rating = rating,
                    HelpfulYesTotal = 0,
                    HelpfulNoTotal = 0,
                    IsApproved = isApproved,
                    CreatedOnUtc = DateTime.UtcNow,
                    StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
                };

                await _productService.InsertProductReviewAsync(productReview);

                //update product totals
                await _productService.UpdateProductReviewTotalsAsync(product);

                //notify store owner
                if (_catalogSettings.NotifyStoreOwnerAboutNewProductReviews)
                {
                    await _workflowMessageService.SendProductReviewNotificationMessageAsync(
                        productReview, _localizationSettings.DefaultAdminLanguageId);
                }

                //activity log
                await _customerActivityService.InsertActivityAsync("PublicStore.AddProductReview",
                    string.Format(
                         await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddProductReview"),
                         product.Name),
                    product);

                //raise event
                if (productReview.IsApproved)
                {
                    await _eventPublisher.PublishAsync(new ProductReviewApprovedEvent(productReview));
                }

                return Ok(new
                {
                    success = true,
                    message = isApproved ?
                        await _localizationService.GetResourceAsync("Reviews.SeeAfterApproving") :
                        await _localizationService.GetResourceAsync("Reviews.SuccessfullyAdded")
                });
            }

            return Ok(new
            {
                success = false,
                message = "Invalid parameters"
            });
        }

        #endregion

        #region Topic

        [HttpGet("get-topic-by-systemname")]
        public virtual async Task<IActionResult> GetTopicBySytemName(string systemName)
        {
            var model = await _topicModelFactory.PrepareTopicModelBySystemNameAsync(systemName);
            return Ok(model);
        }

        #endregion

        #region properties

        public partial class SearchProductByFilters
        {
            //Custom Fields
            public string Keyword { get; set; }
            public bool? PriceHigh { get; set; }
            public bool? PriceLow { get; set; }
            public bool? BestDeals { get; set; }
            public bool? Popular { get; set; }
            public bool? TopRated { get; set; }
            public int? Page { get; set; }
            public int? PageSize { get; set; }
            public int? CategoryId { get; set; }
            public int? VendorId { get; set; }
            public int? ProductId { get; set; }
        }
        public partial class ProductReviewsApiModel : BaseEntity
        {
            public ProductReviewsApiModel()
            {
                Items = new List<ProductReviewsApiModel>();
            }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductSeName { get; set; }
            public string Title { get; set; }
            public string ReviewText { get; set; }
            public int Rating { get; set; }
            public IList<ProductReviewsApiModel> Items { get; set; }
        }
        public partial class AddProductReviewApiModel : BaseEntity
        {
            public string Title { get; set; }
            public string ReviewText { get; set; }
            public int Rating { get; set; }
            public bool DisplayCaptcha { get; set; }
            public bool CanCurrentCustomerLeaveReview { get; set; }
            public bool SuccessfullyAdded { get; set; }
            public string Result { get; set; }
        }

        #endregion
    }
}
