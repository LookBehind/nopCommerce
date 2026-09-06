using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Services.Catalog;
using Nop.Services.Companies;
using Nop.Services.Orders;
using Nop.Services.Vendors;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Cart availability service implementation. Deliberately thin - it composes two
    /// existing signals (vendor working-day/day-off schedule, and Product.Published)
    /// rather than adding a new detection mechanism.
    /// </summary>
    public partial class CartAvailabilityService : ICartAvailabilityService
    {
        #region Fields

        private readonly IShoppingCartService _shoppingCartService;
        private readonly IProductService _productService;
        private readonly IVendorService _vendorService;
        private readonly ICompanyService _companyService;
        private readonly ICompanyVendorScheduleService _companyVendorScheduleService;

        #endregion

        #region Ctor

        public CartAvailabilityService(
            IShoppingCartService shoppingCartService,
            IProductService productService,
            IVendorService vendorService,
            ICompanyService companyService,
            ICompanyVendorScheduleService companyVendorScheduleService)
        {
            _shoppingCartService = shoppingCartService;
            _productService = productService;
            _vendorService = vendorService;
            _companyService = companyService;
            _companyVendorScheduleService = companyVendorScheduleService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the cart items that would be unavailable for the given delivery date
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <param name="selectedDeliveryTimeLocal">Selected delivery time, as stored by the picker (company-local wall-clock time)</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of unavailable cart items (empty if all items are available)
        /// </returns>
        public virtual async Task<IList<UnavailableCartItem>> GetUnavailableItemsAsync(Customer customer, int storeId, DateTime selectedDeliveryTimeLocal)
        {
            var result = new List<UnavailableCartItem>();

            var company = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, storeId);

            foreach (var item in cart)
            {
                var product = await _productService.GetProductByIdAsync(item.ProductId);
                if (product == null)
                    continue;

                if (!product.Published)
                {
                    result.Add(new UnavailableCartItem
                    {
                        CartItemId = item.Id,
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Reason = CartItemUnavailableReason.ProductUnavailable,
                        Message = $"{product.Name} is currently unavailable."
                    });
                    continue;
                }

                if (company == null)
                    continue;

                if (!await _companyVendorScheduleService.IsVendorAvailableAsync(company.Id, product.VendorId, selectedDeliveryTimeLocal.Date))
                {
                    var vendor = await _vendorService.GetVendorByIdAsync(product.VendorId);
                    result.Add(new UnavailableCartItem
                    {
                        CartItemId = item.Id,
                        ProductId = product.Id,
                        ProductName = product.Name,
                        VendorName = vendor?.Name,
                        Reason = CartItemUnavailableReason.VendorClosed,
                        Message = vendor != null
                            ? $"{product.Name} ({vendor.Name}) is not available for delivery on the selected date."
                            : $"{product.Name} is not available for delivery on the selected date."
                    });
                }
            }

            return result;
        }

        #endregion
    }
}
