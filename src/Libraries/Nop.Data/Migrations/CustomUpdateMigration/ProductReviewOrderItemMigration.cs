using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Adds the nullable OrderItemId column to ProductReview so reviews are scoped per order
    /// item instead of per (customer, product) - the same product ordered in two different
    /// orders can then be reviewed independently in each. Existing reviews stay NULL (product-only,
    /// not attributable to a specific order) rather than being backfilled.
    /// </summary>
    [NopMigration("2026-09-01 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class ProductReviewOrderItemMigration : Migration
    {
        public override void Up()
        {
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(ProductReview)))
                    .Column(nameof(ProductReview.OrderItemId))
                    .Exists())
            {
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(ProductReview)))
                    .AddColumn(nameof(ProductReview.OrderItemId)).AsInt32().Nullable();
            }
        }

        public override void Down()
        {
            Delete.Column(nameof(ProductReview.OrderItemId))
                .FromTable(NameCompatibilityManager.GetTableName(typeof(ProductReview)));
        }
    }
}
