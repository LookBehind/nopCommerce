using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Controllers.Api.Security;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.Api.Catalog.v2;

namespace Nop.Web.Controllers.Api.Catalog.v2
{
    [Produces("application/json")]
    [Route("api/v2/catalog")]
    [Authorize]
    public class CatalogApiControllerV2 : BaseApiController
    {
        [HttpGet("product-search")]
        public async Task<ProductsOverviewApiModelV2> SearchProductsAsync(
            SearchProduct searchModel)
        {
            
        }

        [HttpGet("search-filters")]
        public async Task<SearchFiltersModel> SearchFiltersAsync()
        {
            
        }
    }
}