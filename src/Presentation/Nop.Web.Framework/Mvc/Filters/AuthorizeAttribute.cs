using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core.Domain.Customers;
using Nop.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Nop.Web.Framework.Mvc.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // This filter is plain IAsyncAuthorizationFilter, not integrated with ASP.NET
            // Core's own IAuthorizeData/policy system, so the standard [AllowAnonymous] was
            // silently ignored by it - a class-level [AuthorizeAttribute] (CatalogApiController,
            // found live: /api/catalog/favourites 401-ing for guests, breaking App Store
            // Guideline 5.1.1(v) browsing) could never be overridden per-action. Honoring
            // [AllowAnonymous] here makes that override actually work, as it already appears to
            // for every other ASP.NET Core authorization mechanism in this app.
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                return;

            if (!await DataSettingsManager.IsDatabaseInstalledAsync())
                return;

            var user = (Customer)context.HttpContext.Items["User"];
            if (user == null)
            {
                // not logged in
                context.Result = new JsonResult(new { message = "Unauthorized" }) { StatusCode = StatusCodes.Status401Unauthorized };
            }
        }
    }
}
