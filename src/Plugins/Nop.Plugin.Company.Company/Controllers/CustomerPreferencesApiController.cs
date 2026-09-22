using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Plugin.Company.Company.Services;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Company.Company.Controllers
{
    /// <summary>
    /// Mobile-facing customer allergy/undesired-ingredient/avoided-vendor preferences API.
    /// Kept as its own controller in this plugin (mirroring CompanyBalanceApiController's
    /// separation-of-concerns pattern) rather than bloating CustomerApiController.
    /// </summary>
    [Produces("application/json")]
    [Route("api/customer")]
    [Authorize]
    public class CustomerPreferencesApiController(
        ICustomerPreferencesService customerPreferencesService,
        IWorkContext workContext,
        IStoreContext storeContext)
        : BaseApiController
    {
        // Explicit camelCase JsonProperty on every field: ServiceCollectionExtensions.
        // AddNopMvc wires Newtonsoft.Json with a DefaultContractResolver specifically to
        // keep MVC from camel-casing JSON (see its comment there), so a plain POCO here
        // serializes as PascalCase by default - a real contract break against the mobile
        // client's camelCase CustomerPreferences type, found live on mysnacks-dev once
        // mobile-v2 switched this endpoint off its mock. CompanyBalanceApiController
        // never hit this because it hand-builds an anonymous object whose C# property
        // names are already literally camelCase.
        public class CustomerPreferencesApiModel
        {
            [JsonProperty("allergies")]
            public IList<string> Allergies { get; set; } = new List<string>();

            [JsonProperty("undesired")]
            public IList<string> Undesired { get; set; } = new List<string>();

            [JsonProperty("avoidedVendorIds")]
            public IList<int> AvoidedVendorIds { get; set; } = new List<int>();
        }

        [HttpGet("preferences")]
        public async Task<IActionResult> GetPreferences()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var preferences = await customerPreferencesService.GetPreferencesAsync(customer, store.Id);

            return Ok(new CustomerPreferencesApiModel
            {
                Allergies = preferences.Allergies,
                Undesired = preferences.Undesired,
                AvoidedVendorIds = preferences.AvoidedVendorIds
            });
        }

        [HttpPost("preferences")]
        public async Task<IActionResult> SavePreferences([FromBody] CustomerPreferencesApiModel model)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var preferences = new CustomerPreferences
            {
                Allergies = model?.Allergies ?? new List<string>(),
                Undesired = model?.Undesired ?? new List<string>(),
                AvoidedVendorIds = model?.AvoidedVendorIds ?? new List<int>()
            };

            await customerPreferencesService.SavePreferencesAsync(customer, preferences, store.Id);

            return Ok(new CustomerPreferencesApiModel
            {
                Allergies = preferences.Allergies,
                Undesired = preferences.Undesired,
                AvoidedVendorIds = preferences.AvoidedVendorIds
            });
        }
    }
}
