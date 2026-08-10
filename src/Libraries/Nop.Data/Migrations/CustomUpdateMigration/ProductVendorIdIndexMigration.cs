using FluentMigrator;
using Nop.Core.Domain.Catalog;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Any vendor-scoped order search (order list, PDF invoice export, reports) joins
    /// Order/OrderItem/Product and filters Product by VendorId, but Product never had an index on
    /// that column - only as a non-leading key part of IX_GetLowStockProducts (Deleted, VendorId, ...),
    /// which can't be seeked on VendorId alone. At prod-mysnacks scale (40k+ products, 550k+ order
    /// items) that forced a full Product scan on every vendor-scoped order query, which is what was
    /// timing out PDF invoice export for vendors with a large order history.
    /// </summary>
    [NopMigration("2026-08-04 00:00:00:0000000", "Product - add VendorId index")]
    public class ProductVendorIdIndexMigration : AutoReversingMigration
    {
        public override void Up()
        {
            Create.Index("IX_Product_VendorId").OnTable(nameof(Product))
                .OnColumn(nameof(Product.VendorId)).Ascending()
                .WithOptions().NonClustered();
        }
    }
}
