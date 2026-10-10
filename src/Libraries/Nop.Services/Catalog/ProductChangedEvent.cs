using Nop.Core.Domain.Catalog;

namespace Nop.Services.Catalog
{
    /// <summary>
    /// Published after a product edit that changed at least one audited field
    /// (see <see cref="ProductChangeTracker"/>). Consumers (e.g. the Telegram change feed in
    /// Notifications.Manager) get the already-formatted change description.
    /// </summary>
    public class ProductChangedEvent
    {
        public ProductChangedEvent(Product product, string changes, string source, int? customerId, string editorEmail)
        {
            Product = product;
            Changes = changes;
            Source = source;
            CustomerId = customerId;
            EditorEmail = editorEmail;
        }

        public Product Product { get; }

        /// <summary>E.g. "Price: 2500 -> 2630; Published: True -> False"</summary>
        public string Changes { get; }

        /// <summary>"admin" or "integration API"</summary>
        public string Source { get; }

        public int? CustomerId { get; }

        public string EditorEmail { get; }
    }
}
