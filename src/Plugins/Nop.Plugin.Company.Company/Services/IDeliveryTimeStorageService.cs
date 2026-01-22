using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Delivery time storage service interface
    /// </summary>
    public partial interface IDeliveryTimeStorageService
    {
        /// <summary>
        /// Gets the selected delivery time for a customer
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the selected delivery time or null if not set
        /// </returns>
        Task<DateTime?> GetSelectedDeliveryTimeAsync(Customer customer, int storeId);

        /// <summary>
        /// Saves the selected delivery time for a customer
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="deliveryTime">Delivery time to save</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        Task SaveSelectedDeliveryTimeAsync(Customer customer, DateTime deliveryTime, int storeId);

        /// <summary>
        /// Clears the selected delivery time for a customer
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        Task ClearSelectedDeliveryTimeAsync(Customer customer, int storeId);
    }
}
