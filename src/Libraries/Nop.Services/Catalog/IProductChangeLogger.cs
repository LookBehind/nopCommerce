using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;

namespace Nop.Services.Catalog
{
    /// <summary>
    /// Writes the "EditProduct" activity log entry including old -> new values and publishes
    /// <see cref="ProductChangedEvent"/> when something audited actually changed.
    /// </summary>
    public interface IProductChangeLogger
    {
        /// <param name="product">The product after the update was applied</param>
        /// <param name="before">Snapshot taken with <see cref="ProductChangeTracker.Snapshot"/> before the update</param>
        /// <param name="source">Where the change came from, e.g. "admin" or "integration API"</param>
        Task LogEditAsync(Product product, IReadOnlyDictionary<string, string> before, string source);
    }
}
