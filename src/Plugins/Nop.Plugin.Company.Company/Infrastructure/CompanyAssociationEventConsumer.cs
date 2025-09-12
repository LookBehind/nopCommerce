using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Services.Authentication.External;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Logging;
using Nop.Plugin.Company.Company.Services;

namespace Nop.Plugin.Company.Company.Infrastructure
{
    /// <summary>
    /// Event consumer for handling external authentication customer registration events
    /// </summary>
    public class CompanyAssociationEventConsumer : IConsumer<CustomerAutoRegisteredByExternalMethodEvent>
    {
        #region Fields

        private readonly ICompanyService _companyService;
        private readonly ICustomerService _customerService;
        private readonly IAddressService _addressService;
        private readonly ICompanyAddressService _companyAddressService;

        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public CompanyAssociationEventConsumer(
            ICompanyService companyService,
            ICustomerService customerService,
            IAddressService addressService,
            ICompanyAddressService companyAddressService,
            ILogger logger)
        {
            _companyService = companyService;
            _customerService = customerService;
            _addressService = addressService;
            _companyAddressService = companyAddressService;
            _logger = logger;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Handle customer auto registered by external method event
        /// </summary>
        /// <param name="eventMessage">Event message</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task HandleEventAsync(CustomerAutoRegisteredByExternalMethodEvent eventMessage)
        {
            if (eventMessage?.Customer == null || eventMessage.AuthenticationParameters == null)
                return;

            var customer = eventMessage.Customer;

            try
            {
                await _logger.InformationAsync($"Processing external authentication for customer '{customer.Email}' via provider '{eventMessage.AuthenticationParameters.ProviderSystemName}'");

                // Extract email domain from customer email
                if (string.IsNullOrEmpty(customer.Email) || !customer.Email.Contains("@"))
                {
                    await _logger.WarningAsync($"Customer email '{customer.Email}' is invalid or missing @ symbol");
                    return;
                }

                var emailParts = customer.Email.Split('@');
                if (emailParts.Length != 2 || string.IsNullOrEmpty(emailParts[1]))
                {
                    await _logger.WarningAsync($"Customer email '{customer.Email}' could not be split into domain parts");
                    return;
                }

                var emailDomain = emailParts[1];
                await _logger.InformationAsync($"Extracted email domain: '{emailDomain}' from customer email: '{customer.Email}'");

                // Find companies that contain this email domain
                var allCompanies = await _companyService.GetAllCompaniesAsync();
                await _logger.InformationAsync($"Found {allCompanies.Count} total companies to check against");

                // Log all company emails for debugging
                foreach (var company in allCompanies)
                {
                    await _logger.InformationAsync($"Company: '{company.Name}' - Email: '{company.Email}'");
                }

                var matchingCompany = allCompanies.FirstOrDefault(company => 
                    !string.IsNullOrEmpty(company.Email) && 
                    company.Email.Contains(emailDomain, StringComparison.OrdinalIgnoreCase));

                if (matchingCompany == null)
                {
                    await _logger.WarningAsync($"No matching company found for email domain '{emailDomain}'. Customer '{customer.Email}' will not be associated with any company.");
                    return;
                }

                await _logger.InformationAsync($"Found matching company '{matchingCompany.Name}' for customer '{customer.Email}' with domain '{emailDomain}'");

                // Check if customer is already associated with this company
                var existingCompanyCustomer = await _companyService.GetCompanyByCustomerIdAsync(customer.Id);
                if (existingCompanyCustomer != null)
                {
                    await _logger.InformationAsync($"Customer '{customer.Email}' is already associated with company '{existingCompanyCustomer.Name}'");
                    return;
                }

                // Associate customer with the matching company
                var companyCustomer = new Nop.Core.Domain.Companies.CompanyCustomer
                {
                    CompanyId = matchingCompany.Id,
                    CustomerId = customer.Id
                };
                await _companyService.InsertCompanyCustomerAsync(companyCustomer);

                await _logger.InformationAsync($"Associated customer '{customer.Email}' with company '{matchingCompany.Name}'");

                // Activate the customer since they're associated with a company
                if (!customer.Active)
                {
                    customer.Active = true;
                    await _logger.InformationAsync($"Activated customer '{customer.Email}' due to company association");
                }

                // Get all addresses associated with the company
                var companyAddresses = await _companyAddressService.GetCompanyAddressesByCompanyIdAsync(matchingCompany.Id);

                if (!companyAddresses.Any())
                {
                    await _logger.InformationAsync($"No addresses found for company '{matchingCompany.Name}'");
                    // Still update the customer to save the Active status change
                    await _customerService.UpdateCustomerAsync(customer);
                    return;
                }

                var copiedAddresses = new List<int>();

                // Copy each company address to the customer
                foreach (var companyAddress in companyAddresses)
                {
                    // Get the actual address entity
                    var originalAddress = await _addressService.GetAddressByIdAsync(companyAddress.AddressId);
                    if (originalAddress == null)
                        continue;

                    // Clone the address
                    var clonedAddress = _addressService.CloneAddress(originalAddress);
                    clonedAddress.CreatedOnUtc = DateTime.UtcNow;

                    // Insert the cloned address
                    await _addressService.InsertAddressAsync(clonedAddress);

                    // Associate the cloned address with the customer
                    await _customerService.InsertCustomerAddressAsync(customer, clonedAddress);

                    copiedAddresses.Add(clonedAddress.Id);

                    await _logger.InformationAsync($"Copied address (ID: {originalAddress.Id}) from company '{matchingCompany.Name}' to customer '{customer.Email}' (New Address ID: {clonedAddress.Id})");
                }

                // Set the first copied address as both billing and shipping address if customer doesn't have them set
                if (copiedAddresses.Any())
                {
                    var firstAddressId = copiedAddresses.First();
                    
                    if (customer.BillingAddressId == null || customer.BillingAddressId == 0)
                    {
                        customer.BillingAddressId = firstAddressId;
                        await _logger.InformationAsync($"Set billing address (ID: {firstAddressId}) for customer '{customer.Email}'");
                    }

                    if (customer.ShippingAddressId == null || customer.ShippingAddressId == 0)
                    {
                        customer.ShippingAddressId = firstAddressId;
                        await _logger.InformationAsync($"Set shipping address (ID: {firstAddressId}) for customer '{customer.Email}'");
                    }
                }

                // Update the customer with all changes (Active status, billing/shipping addresses)
                await _customerService.UpdateCustomerAsync(customer);

                await _logger.InformationAsync($"Successfully processed customer registration for '{customer.Email}' and associated with company '{matchingCompany.Name}' with {companyAddresses.Count} addresses");
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Error processing customer registration event for customer '{customer.Email}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
