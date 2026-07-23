using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.AmeriaVPos.Services;
using Nop.Web.Framework.Infrastructure.Extensions;

namespace Nop.Plugin.Payments.AmeriaVPos.Infrastructure
{
    /// <summary>
    /// Represents object for the configuring services on application startup
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            //client to request AmeriaBank vPOS services
            services.AddHttpClient<AmeriaVPosApiClient>().WithProxy();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 102;
    }
}
