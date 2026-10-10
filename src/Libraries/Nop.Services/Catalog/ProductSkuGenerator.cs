using System.Text;

namespace Nop.Services.Catalog
{
    /// <summary>
    /// Single source of truth for the auto-generated product SKU format:
    /// "{sanitized vendor name}-{product id}" (e.g. "srtants-11627"). The sanitizer lowercases
    /// the vendor name and strips every whitespace character; all other characters are kept
    /// verbatim. Both the admin product create page (ProductController.GenerateSkuAsync) and the
    /// Company plugin's backfill migration call into here, so they can never drift apart.
    /// </summary>
    public static class ProductSkuGenerator
    {
        /// <summary>
        /// Sanitizes a vendor name for use as a SKU prefix: lowercased, all whitespace removed.
        /// Returns null for a null/whitespace-only name.
        /// </summary>
        /// <param name="vendorName">Vendor name</param>
        /// <returns>The sanitized prefix, or null when there is nothing to derive one from</returns>
        public static string SanitizeVendorName(string vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
                return null;

            var builder = new StringBuilder(vendorName.Length);
            foreach (var ch in vendorName)
            {
                if (char.IsWhiteSpace(ch))
                    continue;

                builder.Append(char.ToLowerInvariant(ch));
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        /// <summary>
        /// Builds the deterministic SKU for a product: "{sanitized vendor name}-{product id}".
        /// </summary>
        /// <param name="vendorName">Vendor name</param>
        /// <param name="productId">Product identifier</param>
        /// <returns>The generated SKU, or null when the vendor name is empty</returns>
        public static string BuildSku(string vendorName, int productId)
        {
            var prefix = SanitizeVendorName(vendorName);
            return prefix == null ? null : $"{prefix}-{productId}";
        }
    }
}
