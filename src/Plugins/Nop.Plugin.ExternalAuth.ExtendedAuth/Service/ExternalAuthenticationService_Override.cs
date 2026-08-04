using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Localization;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Authentication;
using Nop.Services.Authentication.External;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using System;
using System.Threading.Tasks;

namespace Nop.Plugin.ExternalAuth.ExtendedAuth.Service
{
    public partial class ExternalAuthenticationService_Override : ExternalAuthenticationService
    {
        #region Fields

        private readonly ICustomerService _customerService;
        private readonly IWorkContext _workContext;
        private readonly IAuthenticationPluginManager _authenticationPluginManager;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="customerSettings">Customer settings</param>
        /// <param name="externalAuthenticationSettings">External authentication settings</param>
        /// <param name="authenticationService">Authentication service</param>
        /// <param name="customerActivityService">Customer activity service</param>
        /// <param name="customerRegistrationService">Customer registration service</param>
        /// <param name="customerService">Customer service</param>
        /// <param name="eventPublisher">Event publisher</param>
        /// <param name="genericAttributeService">Generic attribute service</param>
        /// <param name="localizationService">Localization service</param>
        /// <param name="pluginService">Plugin finder</param>
        /// <param name="externalAuthenticationRecordRepository">External authentication record repository</param>
        /// <param name="shoppingCartService">Shopping cart service</param>
        /// <param name="storeContext">Store context</param>
        /// <param name="workContext">Work context</param>
        /// <param name="workflowMessageService">Workflow message service</param>
        /// <param name="localizationSettings">Localization settings</param>
        /// <param name="logger">Logger</param>
        public ExternalAuthenticationService_Override(
            CustomerSettings customerSettings,
            ExternalAuthenticationSettings externalAuthenticationSettings,
            IAuthenticationPluginManager authenticationPluginManager,
            ICustomerRegistrationService customerRegistrationService,
            ICustomerService customerService,
            IEventPublisher eventPublisher,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IRepository<ExternalAuthenticationRecord> externalAuthenticationRecordRepository,
            IStoreContext storeContext,
            IWorkContext workContext,
            IWorkflowMessageService workflowMessageService,
            LocalizationSettings localizationSettings,
            ILogger logger
            ) : base(
            customerSettings,
            externalAuthenticationSettings,
            authenticationPluginManager,
            customerRegistrationService,
            customerService,
            eventPublisher,
            genericAttributeService,
            localizationService,
            externalAuthenticationRecordRepository,
            storeContext,
            workContext,
            workflowMessageService,
            localizationSettings,
            logger)
        {
            this._customerService = customerService;
            this._workContext = workContext;
            this._authenticationPluginManager = authenticationPluginManager;
            this._logger = logger;
        }

        #endregion

        #region Method

        /// <summary>
        /// Authenticate user by passed parameters
        /// </summary>
        /// <param name="parameters">External authentication parameters</param>
        /// <param name="returnUrl">URL to which the user will return after authentication</param>
        /// <returns>Result of an authentication</returns>
        public override async Task<IActionResult> AuthenticateAsync(ExternalAuthenticationParameters parameters, string returnUrl = null)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (!await _authenticationPluginManager.IsPluginActiveAsync(parameters.ProviderSystemName))
                return ErrorAuthentication(new[] { "External authentication method cannot be loaded" }, returnUrl);

            //get current logged-in user
            var ambientCustomer = await _workContext.GetCurrentCustomerAsync();
            var ambientIsRegistered = await _customerService.IsRegisteredAsync(ambientCustomer);
            var currentLoggedInUser = ambientIsRegistered ? ambientCustomer : null;

            //authenticate associated user if already exists
            var associatedUser = await GetUserByExternalAuthenticationParametersAsync(parameters);

            //user is already exists or not
            var customerByEmail = await _customerService.GetCustomerByEmailAsync(parameters.Email);

            await _logger.InformationAsync($"ExternalAuth.Override.AuthenticateAsync: provider={parameters.ProviderSystemName} externalEmail='{parameters.Email}' externalId='{parameters.ExternalIdentifier}' -> ambientCustomerId={ambientCustomer?.Id} ambientEmail='{ambientCustomer?.Email}' ambientIsRegistered={ambientIsRegistered} associatedUserId={associatedUser?.Id} associatedUserEmail='{associatedUser?.Email}' customerByEmailId={customerByEmail?.Id} customerByEmailActive={customerByEmail?.Active}");

            if (associatedUser != null)
                return await AuthenticateExistingUserAsync(associatedUser, currentLoggedInUser, returnUrl);

            if (customerByEmail != null)
            {
                // A customer with this email already exists but has NO ExternalAuthenticationRecord
                // for this provider/sub yet - this branch signs them in WITHOUT ever creating that
                // association (no AssociateExternalAccountWithUserAsync call here), which is why a
                // customer can end up permanently missing an ExternalAuthenticationRecord despite
                // logging in via Google successfully.
                await _logger.InformationAsync($"ExternalAuth.Override.AuthenticateAsync: found existing customer Id={customerByEmail.Id} by email '{parameters.Email}' with NO matching ExternalAuthenticationRecord - signing in without creating association (provider={parameters.ProviderSystemName}).");
                return await AuthenticateExistingUserAsync(customerByEmail, currentLoggedInUser, returnUrl);
            }

            //or associate and authenticate new user
            if (returnUrl == "/")
                returnUrl += "customer/info";
            else if (string.IsNullOrEmpty(returnUrl))
                returnUrl = "/customer/info";

            //Save a new record
            // NOTE: currentLoggedInUser is deliberately discarded here (always passes null) even
            // though it was computed above - AuthenticateNewUserAsync's "associate with logged-in
            // user" branch (base class) can never fire from this override; every truly-new external
            // login always goes through RegisterNewUserAsync.
            await _logger.InformationAsync($"ExternalAuth.Override.AuthenticateAsync: no associatedUser, no customerByEmail for '{parameters.Email}' - registering as new user (provider={parameters.ProviderSystemName}, ambientCustomerId={ambientCustomer?.Id}, ambientIsRegistered={ambientIsRegistered}).");
            return await AuthenticateNewUserAsync(null, parameters, returnUrl);
        }

        #endregion
    }
}
