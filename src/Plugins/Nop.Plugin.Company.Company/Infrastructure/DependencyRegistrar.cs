using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Company.Company.Areas.Admin.Factories;
using Nop.Plugin.Company.Company.Controllers;
using Nop.Plugin.Company.Company.Services;
using Nop.Plugin.Company.Company.Services.Reporting;

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
            services.AddScoped<IReportService, ReportService>();
        }

        /// <summary>
        /// Gets order of this dependency registrar implementation
        /// </summary>
        public int Order => 1;
    }
}
