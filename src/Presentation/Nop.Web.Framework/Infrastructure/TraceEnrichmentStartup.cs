using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Core.Infrastructure;
using Nop.Services.Customers;

namespace Nop.Web.Framework.Infrastructure
{
    /// <summary>
    /// Enriches the OpenTelemetry server span (created by the zero-code .NET
    /// auto-instrumentation) with the current MySnacks customer/store context, so
    /// traces in SigNoz can be filtered and correlated by user.
    /// </summary>
    /// <remarks>
    /// No OpenTelemetry SDK is referenced — during a request <see cref="Activity.Current"/>
    /// IS the ASP.NET Core server span, and <see cref="Activity"/> lives in the BCL.
    /// Enrichment must never break a request, hence the broad catch.
    /// <para><c>enduser.id</c> is the nopCommerce CustomerId, which is also the Mixpanel
    /// distinct_id — a clean join key between traces and product analytics.</para>
    /// </remarks>
    public class TraceEnrichmentStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void Configure(IApplicationBuilder application)
        {
            application.Use(async (context, next) =>
            {
                await EnrichAsync(context);
                await next();
            });
        }

        protected static async Task EnrichAsync(HttpContext context)
        {
            var activity = Activity.Current;
            if (activity == null)
                return;

            try
            {
                var services = context.RequestServices;

                var workContext = services.GetService<IWorkContext>();
                var customer = workContext == null ? null : await workContext.GetCurrentCustomerAsync();
                if (customer != null)
                {
                    var customerService = services.GetService<ICustomerService>();

                    activity.SetTag("enduser.id", customer.Id);
                    activity.SetTag("mysnacks.customer.guid", customer.CustomerGuid);

                    if (customerService != null)
                    {
                        activity.SetTag("mysnacks.customer.registered", await customerService.IsRegisteredAsync(customer));

                        var roles = await customerService.GetCustomerRolesAsync(customer);
                        if (roles.Count > 0)
                            activity.SetTag("enduser.role", string.Join(",", roles.Select(r => r.Name)));
                    }

                    if (!string.IsNullOrEmpty(customer.Email))
                        activity.SetTag("mysnacks.customer.email", customer.Email);
                }

                var storeContext = services.GetService<IStoreContext>();
                var store = storeContext == null ? null : await storeContext.GetCurrentStoreAsync();
                if (store != null)
                    activity.SetTag("mysnacks.store.name", store.Name);
            }
            catch
            {
                // telemetry enrichment must never break the request
            }
        }

        /// <summary>
        /// After AuthenticationStartup (500) / AuthorizationStartup (600) so the
        /// customer is resolved, but BEFORE NopMvcStartup (1000) — which registers
        /// the terminal endpoint middleware; anything at/after 1000 would be added
        /// past UseEndpoints and never run for matched routes.
        /// </summary>
        public int Order => 700;
    }
}
