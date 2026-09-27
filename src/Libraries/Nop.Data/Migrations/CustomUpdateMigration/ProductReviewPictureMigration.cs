using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Creates the ProductReviewPicture table - links a ProductReview to the photos a customer
    /// attached to it (see api/catalog/v2/add-product-reviews). MySnacks addition.
    /// </summary>
    [NopMigration("2026-09-27 20:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class ProductReviewPictureMigration : Migration
    {
        private readonly IMigrationManager _migrationManager;

        public ProductReviewPictureMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(ProductReviewPicture))).Exists())
                _migrationManager.BuildTable<ProductReviewPicture>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
