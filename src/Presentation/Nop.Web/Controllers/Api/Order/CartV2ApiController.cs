using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Services.Catalog;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Vendors;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Controllers.Api.Order
{
    /// <summary>
    /// mobile-v2's real, persistent server-side cart - api/v2/cart, versioned alongside
    /// api/v2/catalog for the same reason (see CatalogV2ApiController's header comment).
    ///
    /// Backed directly by IShoppingCartService against the real ShoppingCartType.ShoppingCart
    /// - the same entity/service OrderApiController's own check-products/delete-cart actions
    /// already use for the real (v1) checkout flow - not a new cart concept. mobile-v2 used to
    /// track its own client-side Redux cart (cartSlice.ts's cart/cartCount, added to and
    /// summed entirely in JS) and only synced to the server at final "confirm" time; every
    /// screen now reads/mutates the real server cart directly instead, so the cart badge
    /// count and totals can never drift from what the backend actually has, and a second
    /// device/session would see the same cart.
    ///
    /// AddToCartAsync already merges into an existing matching (same product+attributes)
    /// line by incrementing its quantity rather than always inserting a new row (see
    /// ShoppingCartService.AddToCartAsync), so repeated "+" taps naturally accumulate on one
    /// real line - no separate "does this already exist" check needed here.
    [Produces("application/json")]
    [Route("api/v2/cart")]
    [Authorize]
    public class CartV2ApiController(
        IProductService productService,
        IVendorService vendorService,
        IPictureService pictureService,
        IPriceFormatter priceFormatter,
        IShoppingCartService shoppingCartService,
        IProductAttributeService productAttributeService,
        IProductAttributeParser productAttributeParser,
        ISpecificationAttributeService specificationAttributeService,
        IWorkContext workContext,
        IStoreContext storeContext)
        : BaseApiController
    {
        private const int ThumbnailSize = 300;

        // Same "Ingredients" specification attribute CatalogV2ApiController surfaces as
        // SpecificationLabels on a catalog product - kept here too so the cart's own
        // ConflictBadge (allergy/undesired ingredient check) still works on a line item,
        // not just the avoided-vendor check.
        private const string IngredientsAttributeName = "Ingredients";

        public class CartVendorBriefV2Model
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PictureUrl { get; set; }
        }

        public class CartLineItemV2Model
        {
            public int ShoppingCartItemId { get; set; }
            public int ProductId { get; set; }
            public string Name { get; set; }
            public string ImageUrl { get; set; }
            public CartVendorBriefV2Model Vendor { get; set; }
            public int Quantity { get; set; }
            public decimal UnitPriceValue { get; set; }
            public string UnitPrice { get; set; }
            public decimal LineTotalValue { get; set; }
            public string LineTotal { get; set; }
            public IList<string> SpecificationLabels { get; set; } = new List<string>();
            // Real Product Attribute selections on this line (e.g. "Full", "Balsamic
            // sauce") - just the value names, no attribute-name prefix ("Size: Full") and
            // no HTML, unlike OrderV2ApiController's AttributeInfo - the mobile cart row
            // joins these with ", " itself, same as it already does for SpecificationLabels.
            public IList<string> SelectedAttributeValueNames { get; set; } = new List<string>();
        }

        public class CartV2Model
        {
            public IList<CartLineItemV2Model> Items { get; set; } = new List<CartLineItemV2Model>();
            // Sum of quantities across all lines - what the Cart tab's badge shows, not
            // Items.Count (a customer with 3 of one product should see "3", not "1").
            public int Count { get; set; }
            public decimal Total { get; set; }
            public string TotalFormatted { get; set; }
        }

        public class CartItemAttributeV2Model
        {
            public int ProductAttributeMappingId { get; set; }
            public int ProductAttributeValueId { get; set; }
        }

        public class AddCartItemV2Model
        {
            public int ProductId { get; set; }
            public IList<CartItemAttributeV2Model> SelectedAttributes { get; set; } = new List<CartItemAttributeV2Model>();
        }

        [HttpGet]
        public async Task<IActionResult> GetCart()
        {
            return Ok(await BuildCartModelAsync());
        }

        [HttpPost("items")]
        public async Task<IActionResult> AddItem([FromBody] AddCartItemV2Model model)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var product = await productService.GetProductByIdAsync(model?.ProductId ?? 0);
            if (product == null || product.Deleted)
                return NotFound(new { success = false, message = "Product not found" });

            var attributesXml = await BuildAttributesXmlAsync(product.Id, model?.SelectedAttributes);

            var warnings = await shoppingCartService.AddToCartAsync(
                customer, product, ShoppingCartType.ShoppingCart, store.Id, attributesXml, quantity: 1);

            if (warnings.Count > 0)
                return Ok(new { success = false, errors = warnings, cart = await BuildCartModelAsync() });

            return Ok(new { success = true, cart = await BuildCartModelAsync() });
        }

        // Increments one specific real cart line by its ShoppingCartItemId, unlike
        // AddItem (which resolves/merges by product+attributes). The cart screen's own
        // stepper uses this instead of AddItem so tapping "+" on an attribute-bearing
        // line (e.g. "Half" selected) never risks creating a second, empty-attributes
        // line for the same product - it always adjusts the exact line shown.
        [HttpPost("items/{shoppingCartItemId}/increment")]
        public async Task<IActionResult> IncrementItem(int shoppingCartItemId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var cart = await shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
            var item = cart.FirstOrDefault(i => i.Id == shoppingCartItemId);
            if (item == null)
                return Ok(await BuildCartModelAsync());

            await shoppingCartService.UpdateShoppingCartItemAsync(
                customer, item.Id, item.AttributesXml, item.CustomerEnteredPrice,
                item.RentalStartDateUtc, item.RentalEndDateUtc, item.Quantity + 1);

            return Ok(await BuildCartModelAsync());
        }

        // Mirrors the mobile QuantityStepper's +/- UX exactly (always ±1, never an
        // arbitrary "set to N") - decrement to 0 deletes the line rather than leaving a
        // real ShoppingCartItem row at quantity 0.
        [HttpPost("items/{shoppingCartItemId}/decrement")]
        public async Task<IActionResult> DecrementItem(int shoppingCartItemId)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var cart = await shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
            // Same flat CartModel shape as every other return in this endpoint (and in
            // IncrementItem's own item==null guard just above) - decrementCartItem's mobile
            // client contract (cartApi.ts) expects CartModel directly, not {success, cart}.
            // This branch used to return the wrapped shape, which - reached by a second
            // decrement request landing after the line was already deleted by the first one,
            // a real race on a fast double-tap of "−" - got written verbatim into the getCart
            // RTK Query cache, leaving `cart` truthy but `cart.Items` undefined. Every
            // QuantityStepper.tsx instance on screen then crashed on cart?.Items.filter(...)
            // (Items missing, not cart itself, so the `?.` didn't save it) - confirmed via a
            // live white-screen crash, "Cannot read property 'filter' of undefined".
            var item = cart.FirstOrDefault(i => i.Id == shoppingCartItemId);
            if (item == null)
                return Ok(await BuildCartModelAsync());

            if (item.Quantity > 1)
            {
                await shoppingCartService.UpdateShoppingCartItemAsync(
                    customer, item.Id, item.AttributesXml, item.CustomerEnteredPrice,
                    item.RentalStartDateUtc, item.RentalEndDateUtc, item.Quantity - 1);
            }
            else
            {
                await shoppingCartService.DeleteShoppingCartItemAsync(item.Id);
            }

            return Ok(await BuildCartModelAsync());
        }

        [HttpDelete]
        public async Task<IActionResult> ClearCart()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();
            await shoppingCartService.DeleteShoppingCartItemsAsync(customer, store.Id, ShoppingCartType.ShoppingCart);
            return Ok(await BuildCartModelAsync());
        }

        private async Task<string> BuildAttributesXmlAsync(int productId, IList<CartItemAttributeV2Model> selectedAttributes)
        {
            var attributesXml = "";
            foreach (var selected in selectedAttributes ?? new List<CartItemAttributeV2Model>())
            {
                var mapping = await productAttributeService.GetProductAttributeMappingByIdAsync(selected.ProductAttributeMappingId);
                if (mapping == null || mapping.ProductId != productId)
                    continue;

                attributesXml = productAttributeParser.AddProductAttribute(
                    attributesXml, mapping, selected.ProductAttributeValueId.ToString());
            }
            return attributesXml;
        }

        private async Task<SpecificationAttribute> GetIngredientsAttributeAsync()
        {
            var attributes = await specificationAttributeService.GetSpecificationAttributesAsync();
            return attributes.FirstOrDefault(a => a.Name == IngredientsAttributeName);
        }

        // One upfront pass building {optionId -> name} for just the "Ingredients"
        // attribute's options, so per-line mapping is a dictionary lookup instead of
        // extra sequential DB round trips per mapped specification value - mirrors
        // CatalogV2ApiController.GetIngredientOptionNamesByIdAsync exactly.
        private async Task<Dictionary<int, string>> GetIngredientOptionNamesByIdAsync()
        {
            var ingredientsAttribute = await GetIngredientsAttributeAsync();
            if (ingredientsAttribute == null)
                return new Dictionary<int, string>();

            var options = await specificationAttributeService
                .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(ingredientsAttribute.Id);
            return options.ToDictionary(o => o.Id, o => o.Name);
        }

        // Value names only, in mapping order, flattened across every selected mapping on
        // this line (e.g. Size=Full + Dressing=Balsamic sauce -> ["Full", "Balsamic sauce"]).
        // Mirrors ProductAttributePickerSheet's own value-bearing-only assumption (see that
        // file's header comment) - ParseValues returns raw entered text for a free-text/
        // date/file mapping instead of a value id, which wouldn't parse as one and is
        // silently skipped, matching the picker's existing scope.
        private async Task<IList<string>> GetSelectedAttributeValueNamesAsync(string attributesXml)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(attributesXml))
                return names;

            var mappings = await productAttributeParser.ParseProductAttributeMappingsAsync(attributesXml);
            foreach (var mapping in mappings)
            {
                foreach (var valueIdString in productAttributeParser.ParseValues(attributesXml, mapping.Id))
                {
                    if (!int.TryParse(valueIdString, out var valueId))
                        continue;

                    var value = await productAttributeService.GetProductAttributeValueByIdAsync(valueId);
                    if (value != null)
                        names.Add(value.Name);
                }
            }
            return names;
        }

        private async Task<CartV2Model> BuildCartModelAsync()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();
            var cart = await shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
            var ingredientOptionNames = await GetIngredientOptionNamesByIdAsync();

            var model = new CartV2Model();
            foreach (var item in cart)
            {
                var product = await productService.GetProductByIdAsync(item.ProductId);
                if (product == null || product.Deleted)
                    continue;

                var (unitPrice, _, _) = await shoppingCartService.GetUnitPriceAsync(item, includeDiscounts: true);
                var lineTotal = unitPrice * item.Quantity;

                var specAttributes = await specificationAttributeService.GetProductSpecificationAttributesAsync(
                    product.Id, showOnProductPage: true);
                var specificationLabels = specAttributes
                    .Where(mapping => ingredientOptionNames.ContainsKey(mapping.SpecificationAttributeOptionId))
                    .Select(mapping => ingredientOptionNames[mapping.SpecificationAttributeOptionId])
                    .ToList();

                var pictures = await pictureService.GetPicturesByProductIdAsync(product.Id, 1);
                var imageUrl = pictures.Count > 0
                    ? (await pictureService.GetPictureUrlAsync(pictures[0], ThumbnailSize)).Url
                    : null;

                CartVendorBriefV2Model vendorModel = null;
                if (product.VendorId > 0)
                {
                    var vendor = await vendorService.GetVendorByIdAsync(product.VendorId);
                    if (vendor != null)
                    {
                        var vendorPictureUrl = vendor.PictureId > 0
                            ? await pictureService.GetPictureUrlAsync(vendor.PictureId, ThumbnailSize)
                            : null;
                        vendorModel = new CartVendorBriefV2Model { Id = vendor.Id, Name = vendor.Name, PictureUrl = vendorPictureUrl };
                    }
                }

                model.Items.Add(new CartLineItemV2Model
                {
                    ShoppingCartItemId = item.Id,
                    ProductId = product.Id,
                    Name = product.Name,
                    ImageUrl = imageUrl,
                    Vendor = vendorModel,
                    Quantity = item.Quantity,
                    UnitPriceValue = unitPrice,
                    UnitPrice = await priceFormatter.FormatPriceAsync(unitPrice),
                    LineTotalValue = lineTotal,
                    LineTotal = await priceFormatter.FormatPriceAsync(lineTotal),
                    SpecificationLabels = specificationLabels,
                    SelectedAttributeValueNames = await GetSelectedAttributeValueNamesAsync(item.AttributesXml)
                });

                model.Count += item.Quantity;
                model.Total += lineTotal;
            }

            model.TotalFormatted = await priceFormatter.FormatPriceAsync(model.Total);
            return model;
        }
    }
}
