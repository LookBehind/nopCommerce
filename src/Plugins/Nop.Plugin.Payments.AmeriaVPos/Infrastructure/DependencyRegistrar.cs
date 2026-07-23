using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Payments.AmeriaVPos.Services;
using Nop.Services.Payments;

namespace Nop.Plugin.Payments.AmeriaVPos.Infrastructure
{
    /// <summary>
    /// Dependency registrar
    /// </summary>
    public class DependencyRegistrar : IDependencyRegistrar
    {
        public int Order => 1105;

        public void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddScoped<IAmeriaVPosPaymentService, AmeriaVPosPaymentService>();
        }
    }
}
