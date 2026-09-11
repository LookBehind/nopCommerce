using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Company.Insights.Infrastructure
{
    /// <summary>
    /// Routes for the Insights admin workspace.
    /// </summary>
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {
            // SPA host page + JSON API live under the admin area.
            endpointRouteBuilder.MapControllerRoute("Plugin.Company.Insights",
                "Admin/Insights/{action=Index}/{id?}",
                new { controller = "Insights", area = "Admin" });
        }

        public int Priority => 0;
    }
}
