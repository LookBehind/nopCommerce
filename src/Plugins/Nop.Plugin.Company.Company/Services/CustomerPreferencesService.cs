using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Stores allergy/undesired-ingredient/avoided-vendor preferences as three generic
    /// attributes against <see cref="Customer"/>, each a JSON-encoded array - mirroring the
    /// existing GenericAttributeService-against-Vendor pattern used elsewhere in this codebase
    /// (e.g. NopVendorDefaults.VendorAttributes, VendorTelegramChatCache) rather than new
    /// tables/columns.
    /// </summary>
    public partial class CustomerPreferencesService : ICustomerPreferencesService
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;

        private const string ALLERGIES_KEY = nameof(ALLERGIES_KEY);
        private const string UNDESIRED_KEY = nameof(UNDESIRED_KEY);
        private const string AVOIDED_VENDOR_IDS_KEY = nameof(AVOIDED_VENDOR_IDS_KEY);

        #endregion

        #region Ctor

        public CustomerPreferencesService(IGenericAttributeService genericAttributeService)
        {
            _genericAttributeService = genericAttributeService;
        }

        #endregion

        #region Utility

        private static IList<T> DeserializeOrEmpty<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<T>();

            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }

        #endregion

        #region Methods

        public virtual async Task<CustomerPreferences> GetPreferencesAsync(Customer customer, int storeId)
        {
            var allergiesJson = await _genericAttributeService.GetAttributeAsync<string>(customer, ALLERGIES_KEY, storeId);
            var undesiredJson = await _genericAttributeService.GetAttributeAsync<string>(customer, UNDESIRED_KEY, storeId);
            var avoidedVendorIdsJson = await _genericAttributeService.GetAttributeAsync<string>(customer, AVOIDED_VENDOR_IDS_KEY, storeId);

            return new CustomerPreferences
            {
                Allergies = DeserializeOrEmpty<string>(allergiesJson),
                Undesired = DeserializeOrEmpty<string>(undesiredJson),
                AvoidedVendorIds = DeserializeOrEmpty<int>(avoidedVendorIdsJson)
            };
        }

        public virtual async Task SavePreferencesAsync(Customer customer, CustomerPreferences preferences, int storeId)
        {
            await _genericAttributeService.SaveAttributeAsync(customer,
                ALLERGIES_KEY, JsonSerializer.Serialize(preferences.Allergies ?? new List<string>()), storeId);
            await _genericAttributeService.SaveAttributeAsync(customer,
                UNDESIRED_KEY, JsonSerializer.Serialize(preferences.Undesired ?? new List<string>()), storeId);
            await _genericAttributeService.SaveAttributeAsync(customer,
                AVOIDED_VENDOR_IDS_KEY, JsonSerializer.Serialize(preferences.AvoidedVendorIds ?? new List<int>()), storeId);
        }

        #endregion
    }
}
