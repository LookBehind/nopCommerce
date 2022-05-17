using Nop.Web.Models.Api.Order;

namespace Nop.Web.Models.Api.Catalog.v2
{
    public record SearchFiltersModel(VendorModel[] ByVendors, 
        ProductSpecificationAttributeApiModel[] BySpecificationAttributes
        CategoryModel[] ByCategories);
}