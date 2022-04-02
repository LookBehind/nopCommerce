using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Catalog;
using Nop.Web.Models.Api.Catalog;

namespace Nop.Web.Extensions.Api
{
    public static class ApiModelConversionExtensions
    {
        public static async Task<string> ConvertToAttributesXmlAsync(int productId, 
            ProductOrderSimpleAttribute[] productAttributes, IProductAttributeParser productAttributeParser, 
            IProductAttributeService productAttributeService)
        {
            string attributesXml = string.Empty;
            var attributeMappings = 
                await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);
            
            foreach (var modelAttribute in productAttributes)
            {
                var productAttributeMapping = attributeMappings.FirstOrDefault(am =>
                    am.ProductAttributeId == modelAttribute.ProductAttributeId);

                if (productAttributeMapping == null)
                {
                    throw new NopException("Product doesn't have specified attribute");
                }

                var productAttributeValue = 
                    await productAttributeService.GetProductAttributeValueByIdAsync(
                        modelAttribute.ProductAttributeValueId);

                if (productAttributeValue == null)
                {
                    throw new NopException("Product attribute doesn't have specified value");
                }

                attributesXml = productAttributeParser.AddProductAttribute(attributesXml, productAttributeMapping,
                    productAttributeValue.Id.ToString());
            }

            return attributesXml;
        }
        
        public static async Task<string> ConvertToAttributesXmlAsync(this ProductOrderWithAttributes model,
            IProductAttributeParser productAttributeParser, IProductAttributeService productAttributeService)
        {
            return await ConvertToAttributesXmlAsync(model.ProductId, model.ProductAttributes, productAttributeParser,
                productAttributeService);
        }
        
        
    }
}