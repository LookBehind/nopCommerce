﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Expo.Server.Client;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tax;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Seo;
using Nop.Services.Vendors;
using Nop.Web.Extensions.Api;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.Api.Catalog;
using Nop.Web.Models.Api.Order;
using Nop.Web.Models.Api.Security;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.Order;
using TimeZoneConverter;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Nop.Web.Controllers.Api.Order
{
    [Produces("application/json")]
    [Route("api/order")]
    [Authorize]
    public class OrderApiController : BaseApiController
    {
        #region Fields

        private readonly ICustomerService _customerService;
        private readonly IOrderService _orderService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly ICurrencyService _currencyService;
        private readonly IProductService _productService;
        private readonly ILocalizationService _localizationService;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IPictureService _pictureService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IVendorService _vendorService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly IPaymentService _paymentService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly ICompanyService _companyService;
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly IProductAttributeService _productAttributeService;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly ILogger _logger;
        private readonly ICheckoutAttributeService _checkoutAttributeService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        

        #endregion

        #region Ctor

        public OrderApiController(ICurrencyService currencyService,
            ICustomerService customerService,
            IOrderService orderService,
            IPriceFormatter priceFormatter,
            IPictureService pictureService,
            IUrlRecordService urlRecordService,
            IProductService productService,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            IWorkContext workContext,
            IShoppingCartService shoppingCartService,
            IVendorService vendorService,
            IDateTimeHelper dateTimeHelper,
            IPaymentService paymentService,
            IOrderProcessingService orderProcessingService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ICustomerActivityService customerActivityService,
            ICompanyService companyService,
            IProductAttributeParser productAttributeParser,
            ShoppingCartSettings shoppingCartSettings, 
            IProductAttributeService productAttributeService, 
            ILogger logger, 
            ICheckoutAttributeService checkoutAttributeService, 
            IGenericAttributeService genericAttributeService, 
            ICheckoutAttributeParser checkoutAttributeParser)
        {
            _orderService = orderService;
            _customerService = customerService;
            _priceFormatter = priceFormatter;
            _currencyService = currencyService;
            _pictureService = pictureService;
            _urlRecordService = urlRecordService;
            _productService = productService;
            _shoppingCartService = shoppingCartService;
            _localizationService = localizationService;
            _storeContext = storeContext;
            _workContext = workContext;
            _vendorService = vendorService;
            _dateTimeHelper = dateTimeHelper;
            _paymentService = paymentService;
            _orderProcessingService = orderProcessingService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _customerActivityService = customerActivityService;
            _companyService = companyService;
            _productAttributeParser = productAttributeParser;
            _shoppingCartSettings = shoppingCartSettings;
            _productAttributeService = productAttributeService;
            _logger = logger;
            _checkoutAttributeService = checkoutAttributeService;
            _genericAttributeService = genericAttributeService;
            _checkoutAttributeParser = checkoutAttributeParser;
        }

        #endregion

        #region Utility

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task<IActionResult> GetProductToCartDetailsAsync(List<string> addToCartWarnings, ShoppingCartType cartType,
            Product product)
        {
            if (addToCartWarnings.Any())
            {
                //cannot be added to the cart/wishlist
                //let's display warnings
                return Json(new
                {
                    success = false,
                    message = addToCartWarnings.ToArray()
                });
            }

            //added to the cart/wishlist
            switch (cartType)
            {
                case ShoppingCartType.Wishlist:
                    {
                        //activity log
                        await _customerActivityService.InsertActivityAsync("PublicStore.AddToWishlist",
                            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddToWishlist"), product.Name), product);

                        if (_shoppingCartSettings.DisplayWishlistAfterAddingProduct)
                        {
                            //redirect to the wishlist page
                            return Json(new
                            {
                                redirect = Url.RouteUrl("Wishlist")
                            });
                        }

                        //display notification message and update appropriate blocks
                        var shoppingCarts = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.Wishlist, (await _storeContext.GetCurrentStoreAsync()).Id);

                        var updateTopWishlistSectionHtml = string.Format(
                            await _localizationService.GetResourceAsync("Wishlist.HeaderQuantity"),
                            shoppingCarts.Sum(item => item.Quantity));

                        return Json(new
                        {
                            success = true,
                            message = string.Format(
                                await _localizationService.GetResourceAsync("Products.ProductHasBeenAddedToTheWishlist.Link"),
                                Url.RouteUrl("Wishlist")),
                            updatetopwishlistsectionhtml = updateTopWishlistSectionHtml
                        });
                    }

                case ShoppingCartType.ShoppingCart:
                default:
                    {
                        //activity log
                        await _customerActivityService.InsertActivityAsync("PublicStore.AddToShoppingCart",
                            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddToShoppingCart"), product.Name), product);

                        if (_shoppingCartSettings.DisplayCartAfterAddingProduct)
                        {
                            //redirect to the shopping cart page
                            return Json(new
                            {
                                redirect = Url.RouteUrl("ShoppingCart")
                            });
                        }

                        //display notification message and update appropriate blocks
                        var shoppingCarts = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.ShoppingCart, (await _storeContext.GetCurrentStoreAsync()).Id);

                        var updateTopCartSectionHtml = string.Format(
                            await _localizationService.GetResourceAsync("ShoppingCart.HeaderQuantity"),
                            shoppingCarts.Sum(item => item.Quantity));


                        return Json(new
                        {
                            success = true,
                            message = string.Format(await _localizationService.GetResourceAsync("Products.ProductHasBeenAddedToTheCart.Link"),
                                Url.RouteUrl("ShoppingCart")),
                            updatetopcartsectionhtml = updateTopCartSectionHtml
                        });
                    }
            }
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task SaveItemAsync(ShoppingCartItem updatecartitem, List<string> addToCartWarnings, Product product,
           ShoppingCartType cartType, string attributes, decimal customerEnteredPriceConverted, DateTime? rentalStartDate,
           DateTime? rentalEndDate, int quantity)
        {
            if (updatecartitem == null)
            {
                //add to the cart
                addToCartWarnings.AddRange(await _shoppingCartService.AddToCartAsync(await _workContext.GetCurrentCustomerAsync(),
                    product, cartType, (await _storeContext.GetCurrentStoreAsync()).Id,
                    attributes, customerEnteredPriceConverted,
                    rentalStartDate, rentalEndDate, quantity, true));
            }
            else
            {
                var cart = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), updatecartitem.ShoppingCartType, (await _storeContext.GetCurrentStoreAsync()).Id);

                var otherCartItemWithSameParameters = await _shoppingCartService.FindShoppingCartItemInTheCartAsync(
                    cart, updatecartitem.ShoppingCartType, product, attributes, customerEnteredPriceConverted,
                    rentalStartDate, rentalEndDate);
                if (otherCartItemWithSameParameters != null &&
                    otherCartItemWithSameParameters.Id == updatecartitem.Id)
                {
                    //ensure it's some other shopping cart item
                    otherCartItemWithSameParameters = null;
                }
                //update existing item
                addToCartWarnings.AddRange(await _shoppingCartService.UpdateShoppingCartItemAsync(await _workContext.GetCurrentCustomerAsync(),
                    updatecartitem.Id, attributes, customerEnteredPriceConverted,
                    rentalStartDate, rentalEndDate, quantity + (otherCartItemWithSameParameters?.Quantity ?? 0), true));
                if (otherCartItemWithSameParameters != null && !addToCartWarnings.Any())
                {
                    //delete the same shopping cart item (the other one)
                    await _shoppingCartService.DeleteShoppingCartItemAsync(otherCartItemWithSameParameters);
                }
            }
        }

        protected virtual async Task<List<CartErrorModel>> AddProductsToCartAsync(
            Core.Domain.Customers.Customer customer,
            int storeId,
            ProductOrderRequestApiModel productOrderRequestApiModel)
        {
            var errorList = new List<CartErrorModel>();

            try
            {
                var scheduledDateUTC = await ConvertCustomerLocalTimeToUTCAsync(
                    customer,
                    productOrderRequestApiModel.ScheduleDate);
                
                foreach (var currentProductOrder in productOrderRequestApiModel.Products)
                {
                    try
                    {
                        var product = await _productService.GetProductByIdAsync(currentProductOrder.ProductId);
                        if (product == null)
                        {
                            errorList.Add(new CartErrorModel
                            {
                                Success = false, 
                                Id = currentProductOrder.ProductId,
                                Message = "No product found"
                            });
                        
                            continue;
                        }

                        var attributesXml = await currentProductOrder.ConvertToAttributesXmlAsync(
                            _productAttributeParser, _productAttributeService);

                        //now let's try adding product to the cart (now including product attribute validation, etc)
                        var addToCartWarnings = await _shoppingCartService.AddToCartAsync(customer: customer,
                            product: product,
                            shoppingCartType: ShoppingCartType.ShoppingCart,
                            attributesXml: attributesXml,
                            storeId: storeId,
                            scheduledDateUTC: scheduledDateUTC,
                            quantity: currentProductOrder.Quantity
                        );
                    
                        if (addToCartWarnings.Any())
                        {
                            errorList.Add(new CartErrorModel
                            {
                                Success = false,
                                Id = currentProductOrder.ProductId,
                                Message = string.Join(", ", addToCartWarnings)
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        await _logger.ErrorAsync(
                            $"Failed to add '{JsonSerializer.Serialize(currentProductOrder)}' to the cart", e,
                            customer);
                        
                        errorList.Add(new CartErrorModel
                        {
                            Success = false,
                            Id = currentProductOrder.ProductId,
                            Message = e.Message
                        });
                    }
                }
            }
            catch (Exception e)
            {
                await _logger.ErrorAsync($"Failed to add products to cart. ProductOrderRequestApiModel is '{JsonSerializer.Serialize(productOrderRequestApiModel)}'", e, customer);
                errorList.Add(new CartErrorModel
                {
                    Success = false,
                    Message = "Something went wrong"
                });
            }
            
            return errorList;
        }

        protected virtual async Task LogEditOrderAsync(int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);

            await _customerActivityService.InsertActivityAsync("EditOrder",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
        }

        #endregion

        #region Order
        public class CartErrorModel
        {
            public bool Success { get; set; }
            public int Id { get; set; }
            public string Message { get; set; }
        }

        public class OrderRatingModel
        {
            public int OrderId { get; set; }
            public int Rating { get; set; }
            public string RatingText { get; set; }
        }

        [HttpPost("cancel-order/{id}")]
        public async Task<IActionResult> CancelOrder(int id)
        {
            return Ok(new
            {
                success = false, 
                message = "Cancelling orders is prohibited. Please contact our support in slack."
            });
            
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Order.Cancelled.Failed") });

            //try to get an customer with the order id
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
            if (customer == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("customer.NotFound") });

            await _orderProcessingService.CancelOrderAsync(order, true);
            await LogEditOrderAsync(order.Id);

            if (customer.OrderStatusNotification)
            {
                var expoSDKClient = new PushApiClient();
                var pushTicketReq = new PushTicketRequest()
                {
                    PushTo = new List<string>() { customer.PushToken },
                    PushTitle = await _localizationService.GetResourceAsync("PushNotification.OrderCancelTitle"),
                    PushBody = await _localizationService.GetResourceAsync("PushNotification.OrderCancelBody")
                };
                var result = await expoSDKClient.PushSendAsync(pushTicketReq);
            }

            return Ok(new { success = true, 
                message = await _localizationService.GetResourceAsync("Order.Cancelled.Successfully") });
        }

        [HttpPost("delete-cart/{ids}")]
        public async Task<IActionResult> DeleteCart(string ids)
        {
            int[] cartIds = null;
            if (!string.IsNullOrEmpty(ids))
                cartIds = Array.ConvertAll(ids.Split(","), s => int.Parse(s));

            var carts = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.ShoppingCart, (await _storeContext.GetCurrentStoreAsync()).Id);
            foreach (var sci in carts)
            {
                if (cartIds.Contains(sci.Id))
                    await _shoppingCartService.DeleteShoppingCartItemAsync(sci);
            }
            return Ok(new { success = false, message = "Cart deleted successfully" });
        }

        [HttpPost("check-products")]
        public async Task<IActionResult> CheckProductsAsync(
            [FromBody] ProductOrderRequestApiModel orderRequest)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var currentStoreId = (await _storeContext.GetCurrentStoreAsync()).Id;

            await _shoppingCartService.DeleteShoppingCartItemsAsync(customer,
                currentStoreId, ShoppingCartType.ShoppingCart);

            var cartErrorModel = await AddProductsToCartAsync(customer, currentStoreId, orderRequest);
            
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, 
                ShoppingCartType.ShoppingCart, 
                currentStoreId);

            var cartTotal = await _orderTotalCalculationService.GetShoppingCartTotalAsync(
                cart, false);

            if (cartErrorModel.Any(error => !error.Success))
            {
                return Ok(new
                {
                    success = false,
                    errorList = cartErrorModel.Where(x => !x.Success),
                    cartTotal = cartTotal.shoppingCartTotal
                });
            }

            return Ok(new { 
                success = true, 
                message = "All products are fine", 
                cartTotal = cartTotal.shoppingCartTotal 
            });
        }
        
        private async Task<DateTime> ConvertCustomerLocalTimeToUTCAsync(
            Core.Domain.Customers.Customer customer,
            string date)
        {
            var company = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
            var timezoneInfo = TZConvert.GetTimeZoneInfo(company.TimeZone);
            var dateTimeObject = _dateTimeHelper.ConvertToUtcTime(
                Convert.ToDateTime(date),
                timezoneInfo);
            return dateTimeObject;
        }

        // This is temporary until PlaceOrderAsync is fixed to accept datetime
        private async Task<DateTime> ConvertCustomerLocalTimeToUTCStringAsync(
            Core.Domain.Customers.Customer customer,
            string date)
        {
            return await ConvertCustomerLocalTimeToUTCAsync(customer, date);
        }

        private async Task<bool> IsScheduleDateAllowed(int storeId, Core.Domain.Customers.Customer customer, 
            string customerPreferredScheduleDateString)
        {
            var firstAvailableDeliveryTimes = await _orderProcessingService.GetAvailableDeliverTimesAsync();

            var customerPreferredScheduleDate = Convert.ToDateTime(customerPreferredScheduleDateString);
            if (customerPreferredScheduleDate < firstAvailableDeliveryTimes.First())
            {
                await _logger.WarningAsync($"Schedule date already passed. customerPreferredScheduleDate = {customerPreferredScheduleDate}, " +
                                           $"firstAvailableDeliveryTimes = {string.Join(';', firstAvailableDeliveryTimes)}");
                return false;
            }

            return true;
        }
        
        [HttpPost("order-confirmation/{scheduleDate}")]
        public async Task<IActionResult> OrderConfirmation(string scheduleDate, 
            [FromBody]OrderConfirmationApiModel? orderConfirmationApiModel)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            var scheduleDateUtc = await ConvertCustomerLocalTimeToUTCStringAsync(customer, scheduleDate);
            
            await _logger.InformationAsync($"Ordering at {scheduleDate}", customer: customer);

            var scheduleAllowed = await IsScheduleDateAllowed(store.Id, customer, scheduleDate);
            if (!scheduleAllowed)
            {
                return Ok(new
                {
                    success = false,
                    code = 1000,
                    message =
                        "We're sorry, but looks like your scheduled delivery date had passed (or invalid), please refresh the app, check you schedule date again and confirm the order. " +
                        "If the issue still persist please notify MySnacks team."
                });
            }
            
            // Replace checkout attributes
            if(orderConfirmationApiModel != null)
            {
                var allCheckoutAttributes = await _checkoutAttributeService.GetAllCheckoutAttributesAsync(store.Id);
                
                // Notes
                if(!string.IsNullOrEmpty(orderConfirmationApiModel.Notes))
                {
                    var notesAttribute = allCheckoutAttributes.SingleOrDefault(a =>
                        string.Equals(a.Name, nameof(orderConfirmationApiModel.Notes), StringComparison.OrdinalIgnoreCase));

                    if (notesAttribute != null)
                    {
                        var checkoutAttributesXml =
                            await _genericAttributeService.GetAttributeAsync<string>(customer, 
                                NopCustomerDefaults.CheckoutAttributes,
                                store.Id);

                        checkoutAttributesXml = _checkoutAttributeParser.RemoveCheckoutAttribute(checkoutAttributesXml, 
                            notesAttribute);
                        checkoutAttributesXml = _checkoutAttributeParser.AddCheckoutAttribute(checkoutAttributesXml, 
                            notesAttribute, orderConfirmationApiModel.Notes);
                        
                        await _genericAttributeService.SaveAttributeAsync(customer, 
                            NopCustomerDefaults.CheckoutAttributes,
                            checkoutAttributesXml, 
                            store.Id);
                    }
                    else
                    {
                        await _logger.ErrorAsync("Notes checkout attribute was provided but wasn't found in store configuration.",
                            customer: customer);
                    }
                }
            }
            
            
            var processPaymentRequest = new ProcessPaymentRequest();
            
            _paymentService.GenerateOrderGuid(processPaymentRequest);
            processPaymentRequest.StoreId = store.Id;
            processPaymentRequest.CustomerId = customer.Id;
            processPaymentRequest.ScheduleDate = scheduleDateUtc;

            processPaymentRequest.PaymentMethodSystemName = "Payments.CheckMoneyOrder";
            var placeOrderResult = await _orderProcessingService.PlaceOrderAsync(processPaymentRequest);

            return Ok(new
            {
                success = placeOrderResult.Success,
                message = placeOrderResult.Success ?
                    await _localizationService.GetResourceAsync("Order.Placed.Successfully") :
                    string.Join(", ", placeOrderResult.Errors)
            });
        }
        
        [HttpPost("reorder/{orderId}")]
        public virtual async Task<IActionResult> ReOrderAsync(int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null || order.Deleted ||
                (await _workContext.GetCurrentCustomerAsync()).Id != order.CustomerId)
            {
                return Ok(new
                {
                    success = false, message = await _localizationService.GetResourceAsync("Order.NoOrderFound")
                });
            }

            var productsList = new List<ReOrderResponseApiModel>();
            //move shopping cart items (if possible)
            foreach (var orderItem in await _orderService.GetOrderItemsAsync(order.Id))
            {
                var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
                if (!product.Published || product.Deleted)
                {
                    productsList.Add(new ReOrderResponseApiModel
                    {
                        Success = false,
                        Id = product.Id,
                        Message = product.Name + " is not valid",
                        Quantity = orderItem.Quantity
                    });
                }
                else
                {
                    productsList.Add(new ReOrderResponseApiModel
                    {
                        Success = true, 
                        Id = product.Id, 
                        Quantity = orderItem.Quantity
                    });
                }
            }
            return Ok(new
            {
                success = true, 
                message = await _localizationService.GetResourceAsync("Order.ReOrdered"), 
                productsList = productsList
            });
        }
        [HttpPost("order-rating")]
        public async Task<IActionResult> OrderRatingAsync([FromBody] OrderRatingModel model)
        {
            var order = await _orderService.GetOrderByIdAsync(model.OrderId);
            if (order == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Order.Rating.Failed") });

            order.Rating = model.Rating;
            order.RatingText = model.RatingText;
            await _orderService.UpdateOrderAsync(order);
            return Ok(new { success = true, message = await _localizationService.GetResourceAsync("Order.Rating.Added") });
        }

        [HttpGet("get-todays-orders")]
        public async Task<IActionResult> GetTodaysOrdersAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var orders = await _orderService.SearchOrdersAsync(customerId: customer.Id, sortByDeliveryDate: true);
            var perviousOrders = orders.Where(x => x.ScheduleDate.Date == DateTime.Now.Date).ToList();
            if (perviousOrders.Any())
            {
                var languageId = _workContext.GetWorkingLanguageAsync().Id;
                var model = new CustomerOrderListModel();
                foreach (var order in perviousOrders)
                {
                    var orderModel = new CustomerOrderListModel.OrderDetailsModel
                    {
                        Id = order.Id,
                        ScheduleDate = await _dateTimeHelper.ConvertToUserTimeAsync(order.ScheduleDate, DateTimeKind.Utc),
                        CreatedOn = await _dateTimeHelper.ConvertToUserTimeAsync(order.CreatedOnUtc, DateTimeKind.Utc),
                        OrderStatusEnum = order.OrderStatus,
                        OrderStatus = await _localizationService.GetLocalizedEnumAsync(order.OrderStatus),
                        PaymentStatus = await _localizationService.GetLocalizedEnumAsync(order.PaymentStatus),
                        ShippingStatus = await _localizationService.GetLocalizedEnumAsync(order.ShippingStatus),
                        IsReturnRequestAllowed = await _orderProcessingService.IsReturnRequestAllowedAsync(order),
                        CustomOrderNumber = order.CustomOrderNumber,
                        Rating = order.Rating,
                        RatingText = order.RatingText
                    };
                    var orderTotalInCustomerCurrency = _currencyService.ConvertCurrency(order.OrderTotal, order.CurrencyRate);
                    orderModel.OrderTotal = await _priceFormatter.FormatPriceAsync(orderTotalInCustomerCurrency, true, order.CustomerCurrencyCode, false, _workContext.GetWorkingLanguageAsync().Id);

                    var orderItems = await _orderService.GetOrderItemsAsync(order.Id);

                    foreach (var orderItem in orderItems)
                    {
                        var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
                        var productPicture = await _pictureService.GetPicturesByProductIdAsync(orderItem.ProductId);
                        var vendor = await _vendorService.GetVendorByProductIdAsync(product.Id);
                        var vendorBriefModel = new VendorBriefInfoModel
                        {
                            Id = vendor.Id,
                            Name = await _localizationService.GetLocalizedAsync(vendor, x => x.Name),
                            SeName = await _urlRecordService.GetSeNameAsync(vendor),
                            PictureUrl = await _pictureService.GetPictureUrlAsync(vendor.PictureId)
                        };
                        
                        var orderItemModel = new OrderDetailsModel.OrderItemModel
                        {
                            Id = orderItem.Id,
                            OrderItemGuid = orderItem.OrderItemGuid,
                            Sku = await _productService.FormatSkuAsync(product, orderItem.AttributesXml),
                            VendorName = vendor != null ? vendor.Name : string.Empty,
                            ProductId = product.Id,
                            ProductPictureUrl = productPicture.Any() ? await _pictureService.GetPictureUrlAsync(productPicture.FirstOrDefault().Id) : await _pictureService.GetDefaultPictureUrlAsync(),
                            ProductName = await _localizationService.GetLocalizedAsync(product, x => x.Name),
                            ProductSeName = await _urlRecordService.GetSeNameAsync(product),
                            Quantity = orderItem.Quantity,
                            AttributeInfo = orderItem.AttributeDescription,
                            Vendor = vendorBriefModel
                        };
                        //rental info
                        if (product.IsRental)
                        {
                            var rentalStartDate = orderItem.RentalStartDateUtc.HasValue
                                ? _productService.FormatRentalDate(product, orderItem.RentalStartDateUtc.Value) : "";
                            var rentalEndDate = orderItem.RentalEndDateUtc.HasValue
                                ? _productService.FormatRentalDate(product, orderItem.RentalEndDateUtc.Value) : "";
                            orderItemModel.RentalInfo = string.Format(await _localizationService.GetResourceAsync("Order.Rental.FormattedDate"),
                                rentalStartDate, rentalEndDate);
                        }
                        orderModel.Items.Add(orderItemModel);

                        //unit price, subtotal
                        if (order.CustomerTaxDisplayType == TaxDisplayType.IncludingTax)
                        {
                            //including tax
                            var unitPriceInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.UnitPriceInclTax, order.CurrencyRate);
                            orderItemModel.UnitPrice = await _priceFormatter.FormatPriceAsync(unitPriceInclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, true);

                            var priceInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.PriceInclTax, order.CurrencyRate);
                            orderItemModel.SubTotal = await _priceFormatter.FormatPriceAsync(priceInclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, true);
                        }
                        else
                        {
                            //excluding tax
                            var unitPriceExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.UnitPriceExclTax, order.CurrencyRate);
                            orderItemModel.UnitPrice = await _priceFormatter.FormatPriceAsync(unitPriceExclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, false);

                            var priceExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.PriceExclTax, order.CurrencyRate);
                            orderItemModel.SubTotal = await _priceFormatter.FormatPriceAsync(priceExclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, false);
                        }

                        //downloadable products
                        if (await _orderService.IsDownloadAllowedAsync(orderItem))
                            orderItemModel.DownloadId = product.DownloadId;
                        if (await _orderService.IsLicenseDownloadAllowedAsync(orderItem))
                            orderItemModel.LicenseId = orderItem.LicenseDownloadId ?? 0;
                    }
                    model.Orders.Add(orderModel);
                }
                return Ok(new { success = true, model });
            }
            return Ok(new { success = false, message = "No previous order found" });
        }

        [HttpGet("get-previous-orders")]
        public async Task<IActionResult> GetPreviousOrdersAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var orders = await _orderService.SearchOrdersAsync(customerId: customer.Id, sortByDeliveryDate: true);
            var perviousOrders = orders.Where(x => x.ScheduleDate.Date < DateTime.Now.Date);
            if (perviousOrders.Any())
            {
                var languageId = _workContext.GetWorkingLanguageAsync().Id;
                var model = new CustomerOrderListModel();
                foreach (var order in perviousOrders)
                {
                    var orderModel = new CustomerOrderListModel.OrderDetailsModel
                    {
                        Id = order.Id,
                        CreatedOn = await _dateTimeHelper.ConvertToUserTimeAsync(order.CreatedOnUtc, DateTimeKind.Utc),
                        ScheduleDate = await _dateTimeHelper.ConvertToUserTimeAsync(order.ScheduleDate, DateTimeKind.Utc),
                        OrderStatusEnum = order.OrderStatus,
                        OrderStatus = await _localizationService.GetLocalizedEnumAsync(order.OrderStatus),
                        PaymentStatus = await _localizationService.GetLocalizedEnumAsync(order.PaymentStatus),
                        ShippingStatus = await _localizationService.GetLocalizedEnumAsync(order.ShippingStatus),
                        IsReturnRequestAllowed = await _orderProcessingService.IsReturnRequestAllowedAsync(order),
                        CustomOrderNumber = order.CustomOrderNumber,
                        Rating = order.Rating,
                        RatingText = order.RatingText
                    };
                    var orderTotalInCustomerCurrency = _currencyService.ConvertCurrency(order.OrderTotal, order.CurrencyRate);
                    orderModel.OrderTotal = await _priceFormatter.FormatPriceAsync(orderTotalInCustomerCurrency, true, order.CustomerCurrencyCode, false, _workContext.GetWorkingLanguageAsync().Id);

                    var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
                    
                    foreach (var orderItem in orderItems)
                    {
                        var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
                        var productPicture = await _pictureService.GetPicturesByProductIdAsync(orderItem.ProductId);
                        var vendor = 
                            await _vendorService.GetVendorByProductIdAsync(product.Id) ;
                        
                        var vendorBriefModel = new VendorBriefInfoModel
                        {
                            Id = vendor.Id,
                            Name = await _localizationService.GetLocalizedAsync(vendor, x => x.Name),
                            SeName = await _urlRecordService.GetSeNameAsync(vendor),
                            PictureUrl = await _pictureService.GetPictureUrlAsync(vendor.PictureId)
                        };
                        
                        var orderItemModel = new OrderDetailsModel.OrderItemModel
                        {
                            Id = orderItem.Id,
                            OrderItemGuid = orderItem.OrderItemGuid,
                            Sku = await _productService.FormatSkuAsync(product, orderItem.AttributesXml),
                            VendorName = vendor != null ? vendor.Name : string.Empty,
                            ProductId = product.Id,
                            ProductName = await _localizationService.GetLocalizedAsync(product, x => x.Name),
                            ProductSeName = await _urlRecordService.GetSeNameAsync(product),
                            ProductPictureUrl = productPicture.Any() ? await _pictureService.GetPictureUrlAsync(productPicture.FirstOrDefault().Id) : await _pictureService.GetDefaultPictureUrlAsync(),
                            Quantity = orderItem.Quantity,
                            AttributeInfo = orderItem.AttributeDescription,
                            Vendor = vendorBriefModel
                        };
                        //rental info
                        if (product.IsRental)
                        {
                            var rentalStartDate = orderItem.RentalStartDateUtc.HasValue
                                ? _productService.FormatRentalDate(product, orderItem.RentalStartDateUtc.Value) : "";
                            var rentalEndDate = orderItem.RentalEndDateUtc.HasValue
                                ? _productService.FormatRentalDate(product, orderItem.RentalEndDateUtc.Value) : "";
                            orderItemModel.RentalInfo = string.Format(await _localizationService.GetResourceAsync("Order.Rental.FormattedDate"),
                                rentalStartDate, rentalEndDate);
                        }
                        orderModel.Items.Add(orderItemModel);

                        //unit price, subtotal
                        if (order.CustomerTaxDisplayType == TaxDisplayType.IncludingTax)
                        {
                            //including tax
                            var unitPriceInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.UnitPriceInclTax, order.CurrencyRate);
                            orderItemModel.UnitPrice = await _priceFormatter.FormatPriceAsync(unitPriceInclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, true);

                            var priceInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.PriceInclTax, order.CurrencyRate);
                            orderItemModel.SubTotal = await _priceFormatter.FormatPriceAsync(priceInclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, true);
                        }
                        else
                        {
                            //excluding tax
                            var unitPriceExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.UnitPriceExclTax, order.CurrencyRate);
                            orderItemModel.UnitPrice = await _priceFormatter.FormatPriceAsync(unitPriceExclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, false);

                            var priceExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.PriceExclTax, order.CurrencyRate);
                            orderItemModel.SubTotal = await _priceFormatter.FormatPriceAsync(priceExclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, false);
                        }

                        //downloadable products
                        if (await _orderService.IsDownloadAllowedAsync(orderItem))
                            orderItemModel.DownloadId = product.DownloadId;
                        if (await _orderService.IsLicenseDownloadAllowedAsync(orderItem))
                            orderItemModel.LicenseId = orderItem.LicenseDownloadId ?? 0;
                    }
                    model.Orders.Add(orderModel);
                }
                return Ok(new { success = true, model });
            }
            return Ok(new { success = false, message = "No previous order found" });
        }

        [HttpGet("get-upcoming-orders")]
        public async Task<IActionResult> GetUpcomingOrdersAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var orders = await _orderService.SearchOrdersAsync(customerId: customer.Id);
            var perviousOrders = orders
                .Where(x => x.ScheduleDate.Date > DateTime.Now.Date)
                .OrderBy(x => x.ScheduleDateTime);
            if (perviousOrders.Any())
            {
                var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;
                var model = new CustomerOrderListModel();
                foreach (var order in perviousOrders)
                {
                    var orderModel = new CustomerOrderListModel.OrderDetailsModel
                    {
                        Id = order.Id,
                        CreatedOn = await _dateTimeHelper.ConvertToUserTimeAsync(order.CreatedOnUtc, DateTimeKind.Utc),
                        OrderStatusEnum = order.OrderStatus,
                        OrderStatus = await _localizationService.GetLocalizedEnumAsync(order.OrderStatus),
                        PaymentStatus = await _localizationService.GetLocalizedEnumAsync(order.PaymentStatus),
                        ShippingStatus = await _localizationService.GetLocalizedEnumAsync(order.ShippingStatus),
                        IsReturnRequestAllowed = await _orderProcessingService.IsReturnRequestAllowedAsync(order),
                        CustomOrderNumber = order.CustomOrderNumber,
                        ScheduleDate = await _dateTimeHelper.ConvertToUserTimeAsync(order.ScheduleDate, DateTimeKind.Utc),
                        Rating = order.Rating,
                        RatingText = order.RatingText
                    };
                    var orderTotalInCustomerCurrency = _currencyService.ConvertCurrency(order.OrderTotal, order.CurrencyRate);
                    orderModel.OrderTotal = await _priceFormatter.FormatPriceAsync(orderTotalInCustomerCurrency, true, order.CustomerCurrencyCode, false, _workContext.GetWorkingLanguageAsync().Id);
                    var orderItems = await _orderService.GetOrderItemsAsync(order.Id);

                    foreach (var orderItem in orderItems)
                    {
                        var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
                        var productPicture = await _pictureService.GetPicturesByProductIdAsync(orderItem.ProductId);
                        var vendor = await _vendorService.GetVendorByProductIdAsync(product.Id);
                        
                        var vendorBriefModel = new VendorBriefInfoModel
                        {
                            Id = vendor.Id,
                            Name = await _localizationService.GetLocalizedAsync(vendor, x => x.Name),
                            SeName = await _urlRecordService.GetSeNameAsync(vendor),
                            PictureUrl = await _pictureService.GetPictureUrlAsync(vendor.PictureId)
                        };
                        
                        var orderItemModel = new OrderDetailsModel.OrderItemModel
                        {
                            Id = orderItem.Id,
                            OrderItemGuid = orderItem.OrderItemGuid,
                            Sku = await _productService.FormatSkuAsync(product, orderItem.AttributesXml),
                            VendorName = vendor != null ? vendor.Name : string.Empty,
                            ProductId = product.Id,
                            ProductName = await _localizationService.GetLocalizedAsync(product, x => x.Name),
                            ProductPictureUrl = productPicture.Any() ? await _pictureService.GetPictureUrlAsync(productPicture.FirstOrDefault().Id) : await _pictureService.GetDefaultPictureUrlAsync(),
                            ProductSeName = await _urlRecordService.GetSeNameAsync(product),
                            Quantity = orderItem.Quantity,
                            AttributeInfo = orderItem.AttributeDescription,
                            Vendor = vendorBriefModel
                        };
                        //rental info
                        if (product.IsRental)
                        {
                            var rentalStartDate = orderItem.RentalStartDateUtc.HasValue
                                ? _productService.FormatRentalDate(product, orderItem.RentalStartDateUtc.Value) : "";
                            var rentalEndDate = orderItem.RentalEndDateUtc.HasValue
                                ? _productService.FormatRentalDate(product, orderItem.RentalEndDateUtc.Value) : "";
                            orderItemModel.RentalInfo = string.Format(await _localizationService.GetResourceAsync("Order.Rental.FormattedDate"),
                                rentalStartDate, rentalEndDate);
                        }
                        orderModel.Items.Add(orderItemModel);

                        //unit price, subtotal
                        if (order.CustomerTaxDisplayType == TaxDisplayType.IncludingTax)
                        {
                            //including tax
                            var unitPriceInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.UnitPriceInclTax, order.CurrencyRate);
                            orderItemModel.UnitPrice = await _priceFormatter.FormatPriceAsync(unitPriceInclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, true);

                            var priceInclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.PriceInclTax, order.CurrencyRate);
                            orderItemModel.SubTotal = await _priceFormatter.FormatPriceAsync(priceInclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, true);
                        }
                        else
                        {
                            //excluding tax
                            var unitPriceExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.UnitPriceExclTax, order.CurrencyRate);
                            orderItemModel.UnitPrice = await _priceFormatter.FormatPriceAsync(unitPriceExclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, false);

                            var priceExclTaxInCustomerCurrency = _currencyService.ConvertCurrency(orderItem.PriceExclTax, order.CurrencyRate);
                            orderItemModel.SubTotal = await _priceFormatter.FormatPriceAsync(priceExclTaxInCustomerCurrency, true, order.CustomerCurrencyCode, languageId, false);
                        }

                        //downloadable products
                        if (await _orderService.IsDownloadAllowedAsync(orderItem))
                            orderItemModel.DownloadId = product.DownloadId;
                        if (await _orderService.IsLicenseDownloadAllowedAsync(orderItem))
                            orderItemModel.LicenseId = orderItem.LicenseDownloadId ?? 0;
                    }
                    model.Orders.Add(orderModel);
                }
                return Ok(new { success = true, model });
            }
            return Ok(new { success = false, message = "No upcoming order found" });
        }

        #endregion

    }
}
