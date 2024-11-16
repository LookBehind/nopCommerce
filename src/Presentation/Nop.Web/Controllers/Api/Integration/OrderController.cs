using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Services.Catalog;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Services.Vendors;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Payments;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.Api.Integration;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Nop.Web.Controllers.Integration
{
    [Produces("application/json")]
    [Route("api/integration/{integration}")]
    [Authorize]
    public class OrderController : BaseApiController
    {
        private readonly ICustomerService _customer;
        private readonly IOrderService _order;
        private readonly IWorkContext _workContext;
        private readonly ICompanyService _company;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly ICustomNumberFormatter _numberFormatter;
        private readonly IPermissionService _permission;
        private readonly IStoreContext _storeContext;
        private readonly IPaymentService _paymentService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IProductService _productService;
        private readonly IVendorService _vendorService;
        private readonly ILogger _logger;

        private async Task<IEnumerable<Product>> UpsertProducts(int storeId,
            int vendorId,
            IEnumerable<ExternalProduct> externalProducts)
        {
            var results = new List<Product>();
            
            var existingProductsBySku = (await _productService.SearchProductsAsync(vendorId: vendorId,
                storeId: storeId,
                showHidden: true))
                .ToLookup(p => p.Sku);

            foreach (var externalProduct in externalProducts)
            {
                var existingProduct = existingProductsBySku[externalProduct.Sku].FirstOrDefault();

                if (existingProduct is null)
                {
                    var newProduct = new Product()
                    {
                        Sku = externalProduct.Sku,
                        ShortDescription = externalProduct.ShortDesc,
                        CreatedOnUtc = DateTime.UtcNow,
                        Name = externalProduct.Name,
                        Published = false,
                        Price = externalProduct.Price,
                        VendorId = vendorId,
                        ProductType = ProductType.SimpleProduct,
                        OrderMaximumQuantity = 10,
                        OrderMinimumQuantity = 1,
                        IsShipEnabled = true
                    };
                    
                    await _productService.InsertProductAsync(newProduct);

                    results.Add(newProduct);
                }
                else
                {
                    var updated = false;
                    if (!string.Equals(existingProduct.Name, externalProduct.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        existingProduct.Name = externalProduct.Name;
                        updated = true;
                    }

                    if (!string.Equals(existingProduct.ShortDescription, externalProduct.ShortDesc,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        existingProduct.ShortDescription = externalProduct.ShortDesc;
                        updated = true;
                    }

                    if (existingProduct.Price != externalProduct.Price)
                    {
                        existingProduct.Price = externalProduct.Price;
                        updated = true;
                    }

                    if (updated)
                        await _productService.UpdateProductAsync(existingProduct);

                    results.Add(existingProduct);
                }
            }

            return results;
        }
        
        [HttpPost("order")]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.PaymentRequired)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<IActionResult> Order([FromRoute]string integration, [FromBody]OrderRequest orderRequest)
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var kerpakVendorId = currentCustomer.VendorId;
            
            if (!ModelState.IsValid ||
                !string.Equals(integration, "kerpak", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ErrorMessage("Invalid parameters"));
            }

            await _logger.InformationAsync($"Order from integration {integration}: {JsonSerializer.Serialize(orderRequest)}");

            if (!await _permission.AuthorizeAsync("ExternalOrdersCreation"))
                return Unauthorized(new ErrorMessage("Insufficient permissions"));

            var customer = await _customer.GetCustomerByEmailAsync(orderRequest.CustomerEmail);
            if (customer == null)
                return NotFound(new ErrorMessage("Customer not found"));

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            var targetVendorId = (await _vendorService.GetAllVendorsAsync())
                .FirstOrDefault(v => string.Equals(v.Name, orderRequest.Vendor, StringComparison.OrdinalIgnoreCase))?.Id
                ?? kerpakVendorId;

            if (targetVendorId == kerpakVendorId)
            {
                await _logger.WarningAsync($"Ordering through kerpak and couldn't find vendor with name {orderRequest.Vendor}. " +
                                           $"Possibly invalid name was passed or vendor needs to be added");
            }
            
            var orderProducts = await UpsertProducts(storeId, targetVendorId, orderRequest.Products);
            await _shoppingCartService.DeleteShoppingCartItemsAsync(customer, storeId, ShoppingCartType.ShoppingCart);
            foreach (var orderProduct in orderProducts)
            {
                var warnings = await _shoppingCartService.AddToCartAsync(customer, orderProduct,
                    ShoppingCartType.ShoppingCart,
                    storeId,
                    scheduledDateUTC: DateTime.UtcNow,
                    ignoreNotPublishedWarning: true);

                if (warnings.Any())
                {
                    await _logger.ErrorAsync(
                        $"Failed to add item to cart, item = {orderProduct.Sku}, errors = {string.Join(", ", warnings)}",
                        customer: customer);
                    return Problem("Something went wrong");
                }
            }

            var processPaymentRequest = new ProcessPaymentRequest();
            _paymentService.GenerateOrderGuid(processPaymentRequest);
            processPaymentRequest.StoreId = storeId;
            processPaymentRequest.CustomerId = customer.Id;
            processPaymentRequest.ScheduleDate = DateTime.UtcNow;
            processPaymentRequest.IgnoreNotPublishedWarning = true;

            processPaymentRequest.PaymentMethodSystemName = "Payments.CheckMoneyOrder";
            var placeOrderResult = await _orderProcessingService.PlaceOrderAsync(processPaymentRequest);

            if (!placeOrderResult.Success)
            {
                await _logger.ErrorAsync($"Failed to place order, errors: {string.Join(", ", placeOrderResult.Errors)}", 
                    customer: customer);
            }
            
            return placeOrderResult.Success
                ? Ok()
                : StatusCode(StatusCodes.Status402PaymentRequired, new ErrorMessage("Insufficient funds"));
        }

        [HttpGet("getallowance")]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetAllowance([FromRoute]string integration, 
            [FromQuery]string customerEmail)
        {
            if (!ModelState.IsValid ||
                !string.Equals(integration, "kerpak", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ErrorMessage("Invalid parameters"));
            }

            if (!await _permission.AuthorizeAsync("ExternalOrdersCreation"))
                return Unauthorized(new ErrorMessage("Insufficient permissions"));

            var customer = await _customer.GetCustomerByEmailAsync(customerEmail);
            if (customer == null)
                return NotFound(new ErrorMessage("Customer not found"));

            var allowancePaymentMethod = (ICompanyAllowancePaymentMethod)
                (await _paymentPluginManager.LoadActivePluginsAsyncAsync(customer))
                .Single(p => p is ICompanyAllowancePaymentMethod);

            var remainingAllowance = 
                await allowancePaymentMethod.GetCustomerRemainingAllowance(DateTime.UtcNow, customer);

            return Ok(new { Allowance = remainingAllowance });
        }
        
        [HttpPost("voidallowance")]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorMessage), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<IActionResult> VoidAllowance([FromRoute]string integration, 
            [FromQuery]string customerEmail,
            [FromQuery]DateTime dateUtc)
        {
            if (!ModelState.IsValid ||
                !string.Equals(integration, "servicetitan", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ErrorMessage("Invalid parameters"));
            }

            if (!await _permission.AuthorizeAsync("CompanyAllowanceVoiding"))
                return Unauthorized(new ErrorMessage("Insufficient permissions"));

            var customer = await _customer.GetCustomerByEmailAsync(customerEmail);
            if (customer == null)
                return NotFound(new ErrorMessage("Customer not found"));

            var allowancePaymentMethod = (ICompanyAllowancePaymentMethod)
                (await _paymentPluginManager.LoadActivePluginsAsyncAsync(customer))
                .Single(p => p is ICompanyAllowancePaymentMethod);

            var voided = 
                await allowancePaymentMethod.VoidAllowance(dateUtc, customer);

            await _logger.InformationAsync($"Voided allowance for {customer.Email} for the day {dateUtc}");
            
            return Ok(new { Voided = voided });
        }
        
        public OrderController(ICustomerService customer, 
            IOrderService order, 
            IWorkContext workContext, 
            ICompanyService company, 
            IPaymentPluginManager pluginManager, 
            ICustomNumberFormatter numberFormatter, 
            IPermissionService permission, 
            IStoreContext storeContext, 
            IPaymentService paymentService, 
            IOrderProcessingService orderProcessingService, 
            IShoppingCartService shoppingCartService, 
            IProductService productService, 
            IVendorService vendorService, 
            ILogger logger)
        {
            _customer = customer;
            _order = order;
            _workContext = workContext;
            _company = company;
            _paymentPluginManager = pluginManager;
            _numberFormatter = numberFormatter;
            _permission = permission;
            _storeContext = storeContext;
            _paymentService = paymentService;
            _orderProcessingService = orderProcessingService;
            _shoppingCartService = shoppingCartService;
            _productService = productService;
            _vendorService = vendorService;
            _logger = logger;
        }
    }
}