using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Nop.Plugin.Misc.BuyAmScraper.Models;
using Nop.Services.Catalog;
using Nop.Services.Logging;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Services.Customers;
using Nop.Services.Vendors;
using Nop.Services.Media;
using Nop.Core;
using Nop.Services.Seo;
using HtmlAgilityPack;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Nop.Plugin.Misc.BuyAmScraper.Helpers;
using Nop.Services.Tax;
using Product = Nop.Core.Domain.Catalog.Product;

namespace Nop.Plugin.BuyAmScraper.Service
{
    class AjaxResponse
    {
        public int TotalCount { get; set; }
        public string Listing { get; set; }
        public string Pagination { get; set; }
    }

    class ProductMiniDto
    {
        public string Sku { get; set; }
        public string FullProductUrl { get; set; }
    }

    public class BuyAmScraperTask : Services.Tasks.IScheduleTask
    {
        private const string STD_TAX_CATEGORY_NAME = "Consumables";
        private const string CARREFOUR_CUSTOMER_NAME = "Carrefour";
        private const string CARREFOUR_ALTERNATE_NAME_TO_UNIFY = "Քարֆուր";
        private static readonly Regex _skuExtractor = new Regex("([\\d]+)");
        private readonly string[] _categoryUrlsToScrape;

        private ReaderWriterLockSlim _lastVendorLock = new ReaderWriterLockSlim();
        private Tuple<string, int> _lastVendor;

        private int _taxCategoryId;
        
        private readonly ILogger _logger;
        private readonly IProductService _productService;
        private readonly IVendorService _vendorService;
        private readonly ICategoryService _categoryService;
        private readonly IPictureService _pictureService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly ITaxCategoryService _taxCategoryService;

        private async Task<ProductDTO> Convert(ProductMiniDto miniDto)
        {
            using var httpClient = new WebClient();

            var productPageData = await httpClient.DownloadStringTaskAsync(miniDto.FullProductUrl);

            var productPageHtml = new HtmlDocument();
            productPageHtml.LoadHtml(productPageData);

            var productTitle = productPageHtml.DocumentNode.SelectSingleNode("//h1[@class='product--title']");
            var category = productPageHtml.DocumentNode.SelectSingleNode("//li[@class = 'breadcrumb--entry is--active']//span[@class = 'breadcrumb--title']");
            var subCategory = productPageHtml.DocumentNode.SelectSingleNode("//span[normalize-space()='Բաժին']/following-sibling::a");
            var subSubCategory = productPageHtml.DocumentNode.SelectSingleNode("//span[normalize-space()='Ենթաբաժին']/following-sibling::a");
            var fullDescription = productPageHtml.DocumentNode.SelectSingleNode("//div[contains(@class, 'product--description')]");
            var price = productPageHtml.DocumentNode.SelectSingleNode("//meta[@itemprop='price']");
            var imageUrl = productPageHtml.DocumentNode.SelectSingleNode("//span[@class='image--element']");
            var partner = productPageHtml.DocumentNode.SelectSingleNode("//span[normalize-space()='Գործընկեր՝']/following-sibling::span/a");

            var imageUrlParsed = new Uri(imageUrl.Attributes["data-img-original"].Value);

            var fullDescriptionString = fullDescription?.InnerText?.Trim() ?? string.Empty;
            var shortDescriptionString =
                fullDescriptionString.Substring(0, Math.Min(100, fullDescriptionString.Length))?.Trim();

            return new ProductDTO(imageUrlParsed.ToString()) {
                Name = productTitle.InnerText.Trim(),
                Sku = miniDto.Sku,
                Category = (category?.InnerText ?? subCategory?.InnerText ?? subSubCategory?.InnerText ?? string.Empty).Trim(),
                FullDescription = $"<p>{fullDescriptionString}</p>",
                Price = int.Parse(price.Attributes["content"].Value, System.Globalization.NumberStyles.AllowDecimalPoint),
                ShortDescription = $"<p>{shortDescriptionString}</p>",
                SubCategory = (subCategory?.InnerText ?? subSubCategory?.InnerText ?? string.Empty).Trim(),
                ImageFileName = imageUrlParsed.Segments[imageUrlParsed.Segments.Length - 1],
                Partner = partner?.InnerText?.Trim()
            };
        }

        private async IAsyncEnumerable<ProductMiniDto> ExtractProductsFromDownloadedPages(string categoryUrl)
        {
            using var httpClient = new WebClient();
            string categoryPageData = null;
            try
            {
                categoryPageData = await httpClient.DownloadStringTaskAsync(categoryUrl);
            }
            catch(Exception exc)
            {
                await _logger.ErrorAsync($"Exception while downloading page: {categoryUrl}, message: {exc.Message}");
                yield break;
            }

            var categoryPageHtml = new HtmlDocument();
            categoryPageHtml.LoadHtml(categoryPageData);
            var ajaxRequestUrl = categoryPageHtml.DocumentNode.SelectSingleNode("//form[@id='filter']");
            var ub = new UriBuilder(ajaxRequestUrl.Attributes["data-listing-url"].Value);

            for (int i = 1; i < 500; i++)
            {
                ub.Query = $"p={i}&c={ub.Uri.Segments[ub.Uri.Segments.Length - 1]}&imRootCategoryId=3&o=1&n=100&loadProducts=1";

                AjaxResponse productsResponse = null;
                try
                {
                    productsResponse = JsonSerializer.Deserialize<AjaxResponse>(
                        await httpClient.DownloadStringTaskAsync(ub.Uri),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                }
                catch (Exception exc)
                {
                    await _logger.ErrorAsync($"Exception while getting results from: {ub.Uri}, message: {exc.Message}");
                    yield break;
                }

                var productsPageHtml = new HtmlDocument();
                productsPageHtml.LoadHtml(productsResponse.Listing);

                var productSkusCurrentNodes = productsPageHtml.DocumentNode
                    .SelectNodes("/div[@class='product--box box--minimal']");

                if (productSkusCurrentNodes == null)
                    break;

                var productSkusCurrent = productSkusCurrentNodes
                    .Select(x => {
                        return new ProductMiniDto
                        {
                            Sku = x.Attributes["data-ordernumber"].Value,
                            FullProductUrl = x.SelectSingleNode(".//a[@class='product--title']").Attributes["href"].Value
                        };
                    });
                
                if (productSkusCurrent == null || !productSkusCurrent.Any())
                    break;

                foreach (var productSku in productSkusCurrent)
                {
                    yield return productSku;
                }
            }
        }

        private async Task<Core.Domain.Catalog.Category> GetExactCategoryByName(string name, 
            Core.Domain.Catalog.Category parent = null)
        {
            var categories = await _categoryService.GetAllCategoriesAsync(name);
            return categories.FirstOrDefault(c => 
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) && 
                (parent == null || c.ParentCategoryId == parent.Id));
        }

        private async Task<int> GetOrAddVendor(ProductDTO product)
        {
            string vendorName;
            if (string.IsNullOrWhiteSpace(product.Partner) || product.Partner.Contains(CARREFOUR_ALTERNATE_NAME_TO_UNIFY, StringComparison.OrdinalIgnoreCase))
                vendorName = CARREFOUR_CUSTOMER_NAME;
            else
                vendorName = product.Partner;

            _lastVendorLock.EnterReadLock();
            bool doesVendorMatch = _lastVendor?.Item1?.Equals(vendorName, StringComparison.InvariantCultureIgnoreCase) == true;
            int? matchedId = _lastVendor?.Item2;
            _lastVendorLock.ExitReadLock();

            if (doesVendorMatch)
            {
                return matchedId.Value;
            }

            var vendor = (await _vendorService.GetAllVendorsAsync(vendorName)
                    .ConfigureAwait(false))
                    .FirstOrDefault();
            if (vendor == null)
            {
                vendor = new Core.Domain.Vendors.Vendor()
                {
                    Name = vendorName,
                    Email = $"{vendorName}@mysnacks.shop",
                    Active = true,
                    AdminComment = "Generated with BuyAmScraper plugin"
                };
                await _vendorService.InsertVendorAsync(vendor).ConfigureAwait(false);
            }

            _lastVendorLock.EnterWriteLock();
            _lastVendor = Tuple.Create(vendorName, vendor.Id);
            _lastVendorLock.ExitWriteLock();

            return vendor.Id;
        }

        private async Task<string?> ExtractSku(string productCode)
        {
            var matches = _skuExtractor.Matches(productCode);
            if (matches.Count != 1)
            {
                await _logger.ErrorAsync($"Sku {productCode} doesn't match exactly 1 continous digits format");
                return null;
            }

            return matches[0].Groups[1].Value;
        }
        
        async Task<int> UpsertProducts(IAsyncEnumerable<object> productDTOs)
        {
            int updatedCount = 0;
            await foreach (var productDTOObject in productDTOs)
            {
                var productMiniDTO = productDTOObject as ProductMiniDto;
                var productDTO = productDTOObject as ProductDTO;
                if (productDTO == null && productMiniDTO == null)
                    break;

                var productCode = productDTO?.Sku ?? productMiniDTO.Sku;
                
                try
                {
                    if (productMiniDTO != null)
                        productDTO = await Convert(productMiniDTO);

                    // SKUs have changed, for example from 79857 to CRM-79857
                    productCode = await ExtractSku(productCode);
                    
                    var existingProduct = await _productService.GetProductBySkuAsync(productCode);

                    if (existingProduct != null)
                    {
                        if (!existingProduct.IsEqualHelper(productDTO))
                        {
                            await _logger.InformationAsync($"Updating product with SKU: {existingProduct.Sku}, " +
                                                           $"Old Price: {existingProduct.Price}, New Price: {productDTO.Price}");
                            existingProduct.Update(productDTO);
                            await _productService.UpdateProductAsync(existingProduct);

                            updatedCount++;
                        }

                        continue;
                    }

                    var vendorId = await GetOrAddVendor(productDTO);

                    var product = new Product()
                    {
                        Sku = productDTO.Sku,
                        Name = productDTO.Name,
                        Price = productDTO.Price,
                        ShortDescription = productDTO.ShortDescription,
                        FullDescription = productDTO.FullDescription,
                        VendorId = vendorId,
                        IsShipEnabled = true,
                        DisableWishlistButton = true,
                        Published = true,
                        CreatedOnUtc = DateTime.UtcNow,
                        UpdatedOnUtc = DateTime.UtcNow,
                        RibbonEnable = false,
                        ProductType = Core.Domain.Catalog.ProductType.SimpleProduct,
                        VisibleIndividually = true,
                        ProductTemplateId = 1,
                        OrderMinimumQuantity = 1,
                        OrderMaximumQuantity = 10_000,
                        TaxCategoryId = _taxCategoryId
                    };

                    await _productService.InsertProductAsync(product);

                    var seName = await _urlRecordService.ValidateSeNameAsync(product, null, product.Name, true);
                    await _urlRecordService.SaveSlugAsync(product, seName, 0);

                    var partnerName = !string.IsNullOrWhiteSpace(productDTO.Partner)
                        ? productDTO.Partner
                        : CARREFOUR_CUSTOMER_NAME;
                    var vendorCategory = await GetExactCategoryByName(partnerName);
                    if (vendorCategory == null)
                    {
                        vendorCategory = new Core.Domain.Catalog.Category
                        {
                            Name = partnerName,
                            IncludeInTopMenu = true,
                            CreatedOnUtc = DateTime.Now,
                            UpdatedOnUtc = DateTime.Now,
                            Published = true,
                            AllowCustomersToSelectPageSize = true,
                            PageSizeOptions = "6, 3, 9"
                        };

                        await _categoryService.InsertCategoryAsync(vendorCategory);
                        await _urlRecordService.SaveSlugAsync(vendorCategory,
                            await _urlRecordService.ValidateSeNameAsync(vendorCategory, null, vendorCategory.Name,
                                true), 0);
                    }

                    var productCategory = await GetExactCategoryByName(productDTO.Category, vendorCategory);
                    if (productCategory == null)
                    {
                        productCategory = new Core.Domain.Catalog.Category
                        {
                            Name = productDTO.Category,
                            CreatedOnUtc = DateTime.Now,
                            UpdatedOnUtc = DateTime.Now,
                            ParentCategoryId = vendorCategory.Id,
                            Published = true,
                            AllowCustomersToSelectPageSize = true,
                            PageSizeOptions = "6, 3, 9"
                        };

                        await _categoryService.InsertCategoryAsync(productCategory);
                        await _urlRecordService.SaveSlugAsync(productCategory,
                            await _urlRecordService.ValidateSeNameAsync(productCategory, null, productCategory.Name,
                                true), 0);
                    }

                    bool subCategorySpecified = !string.IsNullOrEmpty(productDTO.SubCategory) &&
                                                !productDTO.SubCategory.Equals(productDTO.Category,
                                                    StringComparison.OrdinalIgnoreCase);
                    var productSubCategory = subCategorySpecified
                        ? await GetExactCategoryByName(productDTO.SubCategory, productCategory)
                        : null;
                    if (subCategorySpecified && productSubCategory == null)
                    {
                        productSubCategory = new Core.Domain.Catalog.Category
                        {
                            Name = productDTO.SubCategory,
                            CreatedOnUtc = DateTime.Now,
                            UpdatedOnUtc = DateTime.Now,
                            ParentCategoryId = productCategory.Id,
                            Published = true,
                            AllowCustomersToSelectPageSize = true,
                            PageSizeOptions = "6, 3, 9"
                        };

                        await _categoryService.InsertCategoryAsync(productSubCategory);
                        await _urlRecordService.SaveSlugAsync(productSubCategory,
                            await _urlRecordService.ValidateSeNameAsync(productSubCategory, null,
                                productSubCategory.Name, true), 0);
                    }

                    await _categoryService.InsertProductCategoryAsync(new Core.Domain.Catalog.ProductCategory
                    {
                        ProductId = product.Id,
                        CategoryId = subCategorySpecified ? productSubCategory.Id : productCategory.Id
                    });

                    var picture = await _pictureService.InsertPictureAsync(productDTO.Image, MimeTypes.ImageJpeg,
                        productDTO.ImageFileName);
                    await _productService.InsertProductPictureAsync(new Core.Domain.Catalog.ProductPicture
                    {
                        PictureId = picture.Id, ProductId = product.Id
                    });

                    await _logger.InformationAsync($"Product Name={productDTO.Name} have been imported");

                    updatedCount++;
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync($"Processing of product with SKU {productCode} failed", e);
                }
            }

            return updatedCount;
        }

        private async Task ScrapeAndAddProducts()
        {
            foreach (var categoryUrl in _categoryUrlsToScrape)
            {
                try
                {
                    var sw = new Stopwatch();
                    sw.Start();

                    var products = ExtractProductsFromDownloadedPages(categoryUrl);
                    var updatedCount = await UpsertProducts(products);
                
                    sw.Stop();
                    await _logger.InformationAsync($"Finished category {categoryUrl}, " +
                                                   $"{updatedCount} products, " +
                                                   $"took {sw.Elapsed.Seconds} seconds");
                }
                catch (Exception e)
                {
                    await _logger.ErrorAsync($"Processing URL {categoryUrl} failed, skipping", e);
                }
            }
        }

        private async Task LeaveOnlyProductsWithHighestPriceOnDuplicates()
        {
            // Duplicates are same SKU or same name
            
            var carrefourVendorIds = (await _vendorService.GetAllVendorsAsync())
                .Where(v => 
                    v.Name?.Contains(CARREFOUR_ALTERNATE_NAME_TO_UNIFY, StringComparison.OrdinalIgnoreCase) == true ||
                    v.Name?.Contains(CARREFOUR_CUSTOMER_NAME, StringComparison.OrdinalIgnoreCase) == true)
                .Select(v => v.Id)
                .ToImmutableHashSet();

            var allProducts = await _productService.SearchProductsAsync();
            var allCarrefourProducts = allProducts
                .Where(p => carrefourVendorIds.Contains(p.VendorId))
                .ToList();
            
            var sameSkuDuplicates = allCarrefourProducts.GroupBy(p => p.Sku);
            var sameNameDuplicates = allCarrefourProducts.GroupBy(p => p.Name);
            var allDuplicates = sameSkuDuplicates.Concat(sameNameDuplicates);
            
            foreach (var grp in allDuplicates)
            {
                var productWithMaxPrice = grp.MaxBy(p => p.Price);
                foreach (var duplicateToUnpublish in grp.Where(p => p != productWithMaxPrice))
                {
                    if (duplicateToUnpublish.Published)
                    {
                        duplicateToUnpublish.Published = false;
                        await _productService.UpdateProductAsync(duplicateToUnpublish);
                        await _logger.InformationAsync(
                            $"Unpublished duplicate {duplicateToUnpublish.Name} (price {duplicateToUnpublish.Price}, max price left published {productWithMaxPrice.Price})");
                    }
                }
            }
        }

        private async Task<int> GetTaxCategoryId()
        {
            var taxCategories = await _taxCategoryService.GetAllTaxCategoriesAsync();
            var standardTaxCategory = taxCategories.FirstOrDefault(tc => string.Equals(tc.Name,
                STD_TAX_CATEGORY_NAME, StringComparison.OrdinalIgnoreCase));

            if (standardTaxCategory == default)
            {
                await _logger.WarningAsync($"Couldn't find tax category {STD_TAX_CATEGORY_NAME}");
                return 0;
            }

            return standardTaxCategory.Id;
        }
        
        public async Task ExecuteAsync()
        {
            _taxCategoryId = await GetTaxCategoryId();
            
            await ScrapeAndAddProducts();
            await LeaveOnlyProductsWithHighestPriceOnDuplicates();
        }
        
        public BuyAmScraperTask(ILogger logger, 
            IProductService productService, 
            IVendorService vendorService,
            ICategoryService categoryService,
            IPictureService pictureService,
            IUrlRecordService urlRecordService, 
            ITaxCategoryService taxCategoryService)
        {
            _categoryUrlsToScrape = new[] {
                "https://buy.am/hy/carrefour/bakery-pastry",
                "https://buy.am/hy/carrefour/fresh-fruit-vegetable",
                "https://buy.am/hy/carrefour/dairy-eggs",
                "https://buy.am/hy/carrefour/frozen-products",
                "https://buy.am/hy/carrefour/breakfast-coffee-tea",
                "https://buy.am/hy/carrefour/bio-organic",
                "https://buy.am/hy/carrefour/sweets-snacks",
                "https://buy.am/hy/carrefour/juices-drinks",
                "https://buy.am/hy/carrefour/alcoholic-beverages-cigarettes",
                "https://buy.am/hy/supermarkets/carrefour/sweets-snacks",

                "https://buy.am/hy/carrefour/household-goods",
                "https://buy.am/hy/12-ktor-pizza?p=1&imRootCategoryId=1316&o=1&n=100&f=9440&sd=9440"
            };
            
            _logger = logger;
            _productService = productService;
            _vendorService = vendorService;
            _categoryService = categoryService;
            _pictureService = pictureService;
            _urlRecordService = urlRecordService;
            _taxCategoryService = taxCategoryService;
        }
    }
}
