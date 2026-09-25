using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Company.Support.Areas.Admin.Factories;
using Nop.Plugin.Company.Support.Services;

namespace Nop.Plugin.Company.Support.Infrastructure
{
    public class DependencyRegistrar : IDependencyRegistrar
    {
        public virtual void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddScoped<ISupportCaseService, SupportCaseService>();
            services.AddScoped<ISupportCaseModelFactory, SupportCaseModelFactory>();
        }

        public int Order => 3;
    }
}
