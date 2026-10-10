using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Services.Catalog;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// Backfills SKUs for products that have none, using the exact rule the admin product create
    /// page applies (ProductController.GenerateSkuAsync): "{sanitized vendor name}-{product id}"
    /// with the vendor name lowercased and whitespace-stripped (e.g. "srtants-11627"). Both sides
    /// share ProductSkuGenerator, so a product created through either path always lands on the
    /// same SKU.
    ///
    /// This migration ONLY fills products whose SKU is blank. It never rewrites a product that
    /// already carries a SKU — external systems and past exports reference those values, and the
    /// historical SKUs were not all produced by this rule (see the PR description), so recomputing
    /// them would be a silent, un-reversible data change.
    ///
    /// Vendorless products (VendorId = 0) get the product id as their SKU. A generated value that
    /// would collide with an existing SKU gets a numeric suffix (-2, -3, ...), mirroring the create
    /// page so both paths converge.
    /// </summary>
    [NopMigration("2026-10-11 00:00:00:0000000", "Company.BackfillProductSkus")]
    [SkipMigrationOnInstall]
    public class BackfillProductSkusMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public BackfillProductSkusMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            // Project to the columns we need — Product.FullDescription is huge and there can be
            // tens of thousands of rows.
            var products = _dataProvider.GetTable<Product>()
                .Where(p => !p.Deleted)
                .Select(p => new { p.Id, p.VendorId, p.Sku })
                .ToList();

            var missing = products.Where(p => string.IsNullOrWhiteSpace(p.Sku)).ToList();
            if (missing.Count == 0)
                return;

            var vendorNames = _dataProvider.GetTable<Vendor>()
                .Select(v => new { v.Id, v.Name })
                .ToList()
                .ToDictionary(v => v.Id, v => v.Name);

            // SKUs already taken by live products, so a generated value never collides. Seeded from
            // the untouched (non-blank) SKUs, then grown as we claim new ones.
            var taken = new HashSet<string>(
                products.Where(p => !string.IsNullOrWhiteSpace(p.Sku)).Select(p => p.Sku),
                StringComparer.OrdinalIgnoreCase);

            foreach (var product in missing)
            {
                vendorNames.TryGetValue(product.VendorId, out var vendorName);
                var sku = ProductSkuGenerator.BuildSku(vendorName, product.Id) ?? product.Id.ToString();

                var candidate = sku;
                var suffix = 1;
                while (!taken.Add(candidate))
                    candidate = $"{sku}-{++suffix}";

                // Targeted single-column update — no need to load the full Product entity.
                _dataProvider.GetTable<Product>()
                    .Where(p => p.Id == product.Id)
                    .Set(p => p.Sku, candidate)
                    .Update();
            }
        }

        public override void Down()
        {
            // No rollback — a generated SKU can't be told apart from a hand-supplied one that
            // happens to match the pattern, and blanking them would undo intentional data.
        }
    }
}
