using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Services.Authentication;
using Nop.Services.Authentication.External;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Orders;

namespace Nop.Web.Controllers.Api.Security
{
    //[Authorize]
    [Produces("application/json")]
    [Route("api/account")]
    public class AccountApiController : BaseApiController
    {
        #region Fields
        private readonly IStoreContext _storeContext;
        private readonly ICustomerRegistrationService _customerRegistrationService;
        private readonly ICustomerService _customerService;
        private readonly CustomerSettings _customerSettings;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly IAuthenticationService _authenticationService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IConfiguration _config;
        private readonly IAddressService _addressService;
        private readonly MediaSettings _mediaSettings;
        private readonly IPictureService _pictureService;
        private readonly ICompanyService _companyService;
        private readonly ILogger _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IExternalAuthenticationService _externalAuthenticationService;

        #endregion

        #region Ctor

        public AccountApiController(ICustomerRegistrationService customerRegistrationService,
            ICustomerService customerService,
            CustomerSettings customerSettings,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            IWorkContext workContext,
            IAuthenticationService authenticationService,
            IShoppingCartService shoppingCartService,
            IConfiguration config,
            IAddressService addressService,
            MediaSettings mediaSettings,
            IPictureService pictureService,
            ICompanyService companyService,
            ILogger logger,
            IHttpContextAccessor httpContextAccessor,
            IExternalAuthenticationService externalAuthenticationService)
        {
            _storeContext = storeContext;
            _customerRegistrationService = customerRegistrationService;
            _customerService = customerService;
            _customerSettings = customerSettings;
            _genericAttributeService = genericAttributeService;
            _localizationService = localizationService;
            _workContext = workContext;
            _authenticationService = authenticationService;
            _shoppingCartService = shoppingCartService;
            _config = config;
            _addressService = addressService;
            _mediaSettings = mediaSettings;
            _pictureService = pictureService;
            _companyService = companyService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _externalAuthenticationService = externalAuthenticationService;
        }

        #endregion

        #region Methods

        public class LoginApiModel
        {
            public string Email { get; set; }
            public string Password { get; set; }
            public string PushToken { get; set; }
            public string GoogleToken { get; set; }
        }

        //to serialize json into class
        public class GoogleTokenClass
        {
            public string iss { get; set; }
            public string azp { get; set; }
            public string aud { get; set; }
            public string sub { get; set; }
            public string email { get; set; }
            public string email_verified { get; set; }
            public string at_hash { get; set; }
            public string name { get; set; }
            public string picture { get; set; }
            public string given_name { get; set; }
            public string family_name { get; set; }
            public string locale { get; set; }
            public string iat { get; set; }
            public string exp { get; set; }
            public string alg { get; set; }
            public string kid { get; set; }
            public string typ { get; set; }
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginApiModel model)
        {
            if (!ModelState.IsValid)
                return Ok(new { success = false, message = GetModelErrors(ModelState) });

            var loginResult = await _customerRegistrationService.ValidateCustomerAsync(model.Email, model.Password);

            //checking if customer comes from google
            if (!string.IsNullOrWhiteSpace(model.GoogleToken))
            {
                //get json from the token url
                var json = new WebClient().DownloadString("https://oauth2.googleapis.com/tokeninfo?id_token=" + model.GoogleToken);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var deserializedGoogleToken = new GoogleTokenClass();
                    try
                    {
                        //deserialized json into Google Token class
                        deserializedGoogleToken = JsonConvert.DeserializeObject<GoogleTokenClass>(json);
                    }
                    catch (Exception)
                    {
                        return Ok(new
                        {
                            success = false,
                            message = await _localizationService.GetResourceAsync("Google.Token.IsNotValid")
                        });
                    }
                    // Use external authentication service for user provisioning
                    var authParameters = new ExternalAuthenticationParameters
                    {
                        ProviderSystemName = "ExternalAuth", // Using Google provider system name
                        Email = deserializedGoogleToken.email,
                        ExternalIdentifier = deserializedGoogleToken.sub,
                        ExternalDisplayIdentifier = deserializedGoogleToken.name,
                        AccessToken = model.GoogleToken,
                        IsApproved = false // Not approved by default, decided by Company plugin
                    };

                    // Add custom claims for additional user info
                    authParameters.Claims.Add(new ExternalAuthenticationClaim("given_name", deserializedGoogleToken.given_name));
                    authParameters.Claims.Add(new ExternalAuthenticationClaim("family_name", deserializedGoogleToken.family_name));
                    authParameters.Claims.Add(new ExternalAuthenticationClaim("picture", deserializedGoogleToken.picture));

                    try
                    {
                        // Authenticate using external service (handles user creation/association)
                        var authResult = await _externalAuthenticationService.AuthenticateAsync(authParameters);
                        
                        // External auth service returns IActionResult for web redirects, 
                        // but we need to extract the user and continue with API response
                        var customer = await _customerService.GetCustomerByEmailAsync(deserializedGoogleToken.email);
                        if (customer != null)
                        {
                            loginResult = CustomerLoginResults.Successful;
                            model.Email = deserializedGoogleToken.email;

                            if (!customer.Active)
                                loginResult = CustomerLoginResults.NotActive;
                        }
                        else
                        {
                            return Ok(new
                            {
                                success = false,
                                message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.CustomerNotExist")
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorAsync("Google authentication failed", ex);
                        return Ok(new
                        {
                            success = false,
                            message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials")
                        });
                    }
                }
            }

            switch (loginResult)
            {
                case CustomerLoginResults.Successful:
                    {
                        var customer = await _customerService.GetCustomerByEmailAsync(model.Email);
                        if (customer == null)
                            return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Customer.Not.Found") });

                        customer.PushToken = model.PushToken;
                        await _customerService.UpdateCustomerAsync(customer);

                        await _workContext.SetCurrentCustomerAsync(customer);

                        //migrate shopping cart
                        await _shoppingCartService.MigrateShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), customer, true);

                        //sign in new customer
                        await _authenticationService.SignInAsync(customer, false);

                        var jwt = new JwtService(_config, _logger);
                        var token = jwt.GenerateSecurityToken(customer.Email, customer.Id);

                        var shippingAddress = customer.ShippingAddressId.HasValue ? await _addressService.GetAddressByIdAsync(customer.ShippingAddressId.Value) : null;

                        var firstName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.FirstNameAttribute);
                        var lastName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.LastNameAttribute);

                        return Ok(new
                        {
                            success = true,
                            message = await _localizationService.GetResourceAsync("Customer.Login.Successfully"),
                            token,
                            pushToken = customer.PushToken,
                            shippingAddress,
                            firstName,
                            customer.Id,
                            customer.Email,
                            lastName,
                            RemindMeNotification = customer.RemindMeNotification,
                            RateReminderNotification = customer.RateReminderNotification,
                            OrderStatusNotification = customer.OrderStatusNotification,
                            avatar = await _pictureService.GetPictureUrlAsync(await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.AvatarPictureIdAttribute), _mediaSettings.AvatarPictureSize, true)
                        });
                    }
                case CustomerLoginResults.CustomerNotExist:
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.CustomerNotExist") });
                case CustomerLoginResults.Deleted:
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.Deleted") });
                case CustomerLoginResults.NotActive:
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.NotActive") });
                case CustomerLoginResults.NotRegistered:
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.NotRegistered") });
                case CustomerLoginResults.LockedOut:
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials.LockedOut") });
                case CustomerLoginResults.WrongPassword:
                default:
                    return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials") });
            }
        }

        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null)
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Customer.Not.Found") });

            //customer.PushToken = null;
            //await _customerService.UpdateCustomerAsync(customer);

            //standard logout 
            await _authenticationService.SignOutAsync();

            return Ok(new { success = true, message = await _localizationService.GetResourceAsync("Customer.Logout.Successfully") });
        }

        [AllowAnonymous]
        [HttpGet("check-customer-token")]
        public async Task<IActionResult> CheckCustomerToken()
        {
            // TODO: FIX THIS!
            if (_httpContextAccessor.HttpContext?.Items.TryGetValue("User", out var customerObj) != true ||
                customerObj is not Nop.Core.Domain.Customers.Customer customer)
            {
                return Ok(new { success = false, message = await _localizationService.GetResourceAsync("Account.Login.WrongCredentials") });
            }
            
            var jwt = new JwtService(_config, _logger);
            
            var token = jwt.GenerateSecurityToken(customer.Email, customer.Id);
            
            var shippingAddress = customer.ShippingAddressId.HasValue ? await _addressService.GetAddressByIdAsync(customer.ShippingAddressId.Value) : null;
            var firstName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.FirstNameAttribute);
            var lastName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.LastNameAttribute);
            
            return Ok(new
            {
                success = true,
                token,
                pushToken = customer.PushToken,
                shippingAddress,
                firstName,
                lastName,
                RemindMeNotification = customer.RemindMeNotification,
                RateReminderNotification = customer.RateReminderNotification,
                OrderStatusNotification = customer.OrderStatusNotification,
                avatar = await _pictureService.GetPictureUrlAsync(
                    await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.AvatarPictureIdAttribute), 
                    _mediaSettings.AvatarPictureSize, true)
            });
        }

        #endregion
    }
}
