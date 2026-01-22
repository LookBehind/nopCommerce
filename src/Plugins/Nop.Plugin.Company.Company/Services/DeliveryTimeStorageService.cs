using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Delivery time storage service implementation
    /// </summary>
    public partial class DeliveryTimeStorageService : IDeliveryTimeStorageService
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private const string SELECTED_DELIVERY_TIME_KEY = nameof(SELECTED_DELIVERY_TIME_KEY);

        #endregion

        #region Ctor

        public DeliveryTimeStorageService(IGenericAttributeService genericAttributeService)
        {
            _genericAttributeService = genericAttributeService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the selected delivery time for a customer
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the selected delivery time or null if not set
        /// </returns>
        public virtual async Task<DateTime?> GetSelectedDeliveryTimeAsync(Customer customer, int storeId)
        {
            return await _genericAttributeService.GetAttributeAsync<DateTime?>(
                customer, SELECTED_DELIVERY_TIME_KEY, storeId);
        }

        /// <summary>
        /// Saves the selected delivery time for a customer
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="deliveryTime">Delivery time to save</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual async Task SaveSelectedDeliveryTimeAsync(Customer customer, DateTime deliveryTime, int storeId)
        {
            await _genericAttributeService.SaveAttributeAsync(customer,
                SELECTED_DELIVERY_TIME_KEY, deliveryTime, storeId);
        }

        /// <summary>
        /// Clears the selected delivery time for a customer
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual async Task ClearSelectedDeliveryTimeAsync(Customer customer, int storeId)
        {
            await _genericAttributeService.SaveAttributeAsync<DateTime?>(customer,
                SELECTED_DELIVERY_TIME_KEY, null, storeId);
        }

        #endregion
    }
}
