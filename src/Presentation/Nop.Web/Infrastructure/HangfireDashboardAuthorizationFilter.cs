using Hangfire.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Nop.Services.Security;

namespace Nop.Web.Infrastructure
{
    /// <summary>
    /// Restricts the Hangfire dashboard (/hangfire) to admins holding the <see cref="StandardPermissionProvider.ManageScheduleTasks"/>
    /// permission - the same gate as the admin "Schedule tasks" page. This guards the route itself; the admin
    /// menu link is independently rendered only for authorized users (see Areas/Admin/sitemap.config).
    /// </summary>
    public partial class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            //resolve from the request scope so the current (cookie-authenticated) admin is used
            var permissionService = httpContext?.RequestServices?.GetService<IPermissionService>();
            if (permissionService == null)
                return false;

            return permissionService.AuthorizeAsync(StandardPermissionProvider.ManageScheduleTasks)
                .GetAwaiter().GetResult();
        }
    }
}
