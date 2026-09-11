using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Adds triage/approval tracking columns to ProductReview so we can report WHO approved/triaged a
    /// review (TriagedByCustomerId), WHEN (TriagedOnUtc), and the RESOLUTION notes (ResolutionDetails).
    /// Triage time is derived as TriagedOnUtc - CreatedOnUtc (not stored). Schema-API only (no raw SQL),
    /// so it runs on both SQL Server (prod) and PostgreSQL (dev).
    /// </summary>
    [NopMigration("2026-09-11 10:00:00:0000000", "MySnacks: ProductReview triage/approval tracking columns")]
    [SkipMigrationOnInstall]
    public class ProductReviewTriageMigration : Migration
    {
        public override void Up()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(ProductReview));
            var table = Schema.Table(tableName);
            if (!table.Exists())
                return;

            if (!table.Column(nameof(ProductReview.TriagedByCustomerId)).Exists())
                Alter.Table(tableName).AddColumn(nameof(ProductReview.TriagedByCustomerId)).AsInt32().Nullable();

            if (!table.Column(nameof(ProductReview.TriagedOnUtc)).Exists())
                Alter.Table(tableName).AddColumn(nameof(ProductReview.TriagedOnUtc)).AsDateTime2().Nullable();

            if (!table.Column(nameof(ProductReview.ResolutionDetails)).Exists())
                Alter.Table(tableName).AddColumn(nameof(ProductReview.ResolutionDetails)).AsString(4000).Nullable();
        }

        public override void Down()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(ProductReview));
            var table = Schema.Table(tableName);
            if (!table.Exists())
                return;

            if (table.Column(nameof(ProductReview.ResolutionDetails)).Exists())
                Delete.Column(nameof(ProductReview.ResolutionDetails)).FromTable(tableName);
            if (table.Column(nameof(ProductReview.TriagedOnUtc)).Exists())
                Delete.Column(nameof(ProductReview.TriagedOnUtc)).FromTable(tableName);
            if (table.Column(nameof(ProductReview.TriagedByCustomerId)).Exists())
                Delete.Column(nameof(ProductReview.TriagedByCustomerId)).FromTable(tableName);
        }
    }
}
