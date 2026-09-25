using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Support.Domain;
using Nop.Plugin.Company.Support.Services;
using Nop.Services.Vendors;
using Nop.Web.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Company.Support.Controllers
{
    /// <summary>
    /// mobile-v2's own support-case inquiries surface - customer-submitted, staff-managed via
    /// the plugin's Admin > Support Cases queue (SupportCaseController). Was a stub toast on
    /// Profile's "Help & Support" row.
    /// </summary>
    [Produces("application/json")]
    [Route("api/v2/support")]
    [Authorize]
    public class SupportV2ApiController(
        ISupportCaseService supportCaseService,
        IVendorService vendorService,
        IWorkContext workContext,
        IStoreContext storeContext)
        : BaseApiController
    {
        public class SupportCaseV2Model
        {
            public int Id { get; set; }
            public string Category { get; set; }
            public string VendorName { get; set; }
            public string Subject { get; set; }
            public string Description { get; set; }
            public string Status { get; set; }
            public DateTime CreatedOnUtc { get; set; }
        }

        public class CreateSupportCaseV2Model
        {
            public string Category { get; set; }
            public int? VendorId { get; set; }
            public string Subject { get; set; }
            public string Description { get; set; }
        }

        [HttpGet("cases")]
        public async Task<IActionResult> GetCases()
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var cases = await supportCaseService.GetSupportCasesByCustomerIdAsync(customer.Id, store.Id);
            var result = new List<SupportCaseV2Model>(cases.Count);
            foreach (var supportCase in cases)
                result.Add(await MapAsync(supportCase));

            return Ok(result);
        }

        [HttpGet("cases/{id:int}")]
        public async Task<IActionResult> GetCase(int id)
        {
            var customer = await workContext.GetCurrentCustomerAsync();
            var supportCase = await supportCaseService.GetSupportCaseByIdAsync(id);
            if (supportCase == null || supportCase.CustomerId != customer.Id)
                return NotFound();

            return Ok(await MapAsync(supportCase));
        }

        [HttpPost("cases")]
        public async Task<IActionResult> CreateCase([FromBody] CreateSupportCaseV2Model model)
        {
            if (!Enum.TryParse<SupportCaseCategory>(model?.Category, out var category))
            {
                return Ok(new
                {
                    success = false,
                    message = "Unknown category. Expected Technical, VendorQuality, NewVendorRequest, DeliveryInquiry, or BillingInquiry."
                });
            }

            if (string.IsNullOrWhiteSpace(model?.Subject) || string.IsNullOrWhiteSpace(model?.Description))
                return Ok(new { success = false, message = "Subject and description are both required." });

            // Vendor Quality is the one category that requires a real, existing vendor -
            // every other category always stores VendorId: null, even if one was sent.
            int? vendorId = null;
            if (category == SupportCaseCategory.VendorQuality)
            {
                if (!model.VendorId.HasValue)
                    return Ok(new { success = false, message = "Please select which vendor this is about." });

                var vendor = await vendorService.GetVendorByIdAsync(model.VendorId.Value);
                if (vendor == null || vendor.Deleted)
                    return Ok(new { success = false, message = "That vendor could not be found." });

                vendorId = vendor.Id;
            }

            var customer = await workContext.GetCurrentCustomerAsync();
            var store = await storeContext.GetCurrentStoreAsync();

            var supportCase = await supportCaseService.InsertSupportCaseAsync(new SupportCase
            {
                CustomerId = customer.Id,
                StoreId = store.Id,
                Category = category,
                VendorId = vendorId,
                Subject = model.Subject.Trim(),
                Description = model.Description.Trim()
            });

            return Ok(new { success = true, supportCase = await MapAsync(supportCase) });
        }

        private async Task<SupportCaseV2Model> MapAsync(SupportCase supportCase)
        {
            string vendorName = null;
            if (supportCase.VendorId.HasValue)
            {
                var vendor = await vendorService.GetVendorByIdAsync(supportCase.VendorId.Value);
                vendorName = vendor?.Name;
            }

            return new SupportCaseV2Model
            {
                Id = supportCase.Id,
                Category = supportCase.Category.ToString(),
                VendorName = vendorName,
                Subject = supportCase.Subject,
                Description = supportCase.Description,
                Status = supportCase.Status.ToString(),
                CreatedOnUtc = supportCase.CreatedOnUtc
            };
        }
    }
}
