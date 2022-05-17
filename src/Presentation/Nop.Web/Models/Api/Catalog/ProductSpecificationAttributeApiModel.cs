using System.Collections.Generic;
using Nop.Web.Framework.Models;
using Nop.Web.Models.Catalog;

namespace Nop.Web.Models.Api.Catalog
{
    public record ProductSpecificationAttributeApiModel : BaseNopEntityModel
    {
        public string Name { get; set; }

        public IList<ProductSpecificationAttributeValueApiModel> Values { get; init; } 
            = new List<ProductSpecificationAttributeValueApiModel>();
    }
}