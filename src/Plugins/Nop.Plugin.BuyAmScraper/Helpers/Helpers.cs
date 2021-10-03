using System;
using System.Resources;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.BuyAmScraper.Models;

namespace Nop.Plugin.Misc.BuyAmScraper.Helpers
{
    public static class Helpers
    {
        public static bool IsEqualHelper(this Product left, ProductDTO right)
        {
            return
                string.Equals(left.Name, right.Name, StringComparison.InvariantCulture) &&
                string.Equals(left.ShortDescription, right.ShortDescription, StringComparison.InvariantCulture) &&
                string.Equals(left.FullDescription, right.FullDescription, StringComparison.InvariantCulture) &&
                left.Price == right.Price;
        }

        public static void Update(this Product product, ProductDTO productDto)
        {
            product.Name = productDto.Name;
            product.ShortDescription = productDto.ShortDescription;
            product.FullDescription = productDto.FullDescription;
            product.UpdatedOnUtc = DateTime.UtcNow;
            product.Price = productDto.Price;
        }
    }
}