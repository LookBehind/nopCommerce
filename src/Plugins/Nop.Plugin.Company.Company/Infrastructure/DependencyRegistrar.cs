using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Company.Company.Areas.Admin.Factories;
using Nop.Plugin.Company.Company.Controllers;
using Nop.Plugin.Company.Company.Factories;
using Nop.Plugin.Company.Company.Services;

namespace Nop.Plugin.Company.Company.Infrastructure
{
    /// <summary>
    /// Dependency registrar
    /// </summary>
    public class DependencyRegistrar : IDependencyRegistrar
    {
        /// <summary>
        /// Register services and interfaces
        /// </summary>
        /// <param name="services">Collection of service descriptors</param>
        /// <param name="typeFinder">Type finder</param>
        /// <param name="appSettings">App settings</param>
        public virtual void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddScoped<ICompanyModelFactory, CompanyModelFactory>();
            services.AddScoped<ICompanyAddressService, CompanyAddressService>();
            services.AddScoped<IDeliveryTimeService, DeliveryTimeService>();
            services.AddScoped<IDeliveryTimeStorageService, DeliveryTimeStorageService>();
            services.AddScoped<IGlobalDeliveryTimeValidationService, GlobalDeliveryTimeValidationService>();
            services.AddScoped<Nop.Web.Controllers.CheckoutController, CheckoutController_Overriden>();
            services.AddScoped<Nop.Web.Factories.ICheckoutModelFactory, CheckoutModelFactory_Overriden>();
        }

        /// <summary>
        /// Gets order of this dependency registrar implementation. Must run AFTER
        /// Nop.Web's core registrar (Order = 2, registers ICheckoutModelFactory ->
        /// CheckoutModelFactory) - dependency registrars run in ascending Order, and
        /// the last AddScoped registration for a given interface wins. At Order = 1
        /// this registrar's ICheckoutModelFactory -> CheckoutModelFactory_Overriden
        /// registration ran first and was silently overwritten by the core one, so
        /// the override never actually executed despite compiling and looking correct.
        /// </summary>
        public int Order => 3;
    }
}
