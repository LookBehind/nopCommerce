namespace Nop.Web.Models.Api.Catalog.v2
{
    // search criteria
    // ((keyword) || (VendorId1 && ... && VendorIdN) || (CategoryId1 && ... && CategoryIdN) || ...)
    public record SearchProduct(string Keyword, 
        int[] VendorIds, 
        int[] CategoryIds, 
        ProductOrderSimpleAttribute[] ProductSpecificationAttributes /* Product specification attributes, i.e. pork, beef, etc */);
}