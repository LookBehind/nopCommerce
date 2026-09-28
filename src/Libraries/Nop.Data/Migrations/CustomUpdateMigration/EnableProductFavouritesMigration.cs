using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Configuration;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// The mobile-v2 Favourites feature (piggybacks on nopCommerce's stock Wishlist cart type -
    /// see CatalogApiController's Favourites region) turned out to be completely broken for every
    /// customer, for two independent, stacked reasons found live-testing on mysnacks-dev
    /// 2026-09-28 (neither a deliberate configuration choice - no per-product/per-store reason was
    /// ever recorded for either):
    ///
    /// 1. ShoppingCartSettings.MaximumWishlistItems (one store-wide Setting row, not per-product)
    ///    was 0, so every add-to-wishlist call failed store-wide ("The maximum number of distinct
    ///    products allowed in the wishlist is 0"), regardless of which product. nopCommerce's own
    ///    install seed (InstallationService.cs) ships this at 1000 - restored to that same value
    ///    rather than inventing a new number.
    ///
    /// 2. Product.DisableWishlistButton was true for 954 of 1060 published products (90% of the
    ///    catalog) - most likely a stale default from the original bulk product import. Cleared
    ///    store-wide so every product can be favourited. Nothing in this codebase should set this
    ///    flag back to true going forward - see the removed admin editor checkbox
    ///    (_CreateOrUpdate.Prices.cshtml) and ProductService.ApplyLowStockActivityAsync, both
    ///    updated alongside this migration.
    ///
    /// The product fix is a plain set-based UPDATE, not an entity-by-entity
    /// _dataProvider.UpdateEntitiesAsync loop - a first attempt at that crash-looped mysnacks-dev
    /// on every startup ("The query processor ran out of internal resources and could not produce
    /// a query plan"): LINQ2DB's entity-batch update over 954 rows built a single SQL Server
    /// statement too complex to compile. A one-line UPDATE ... WHERE has no such limit regardless
    /// of row count.
    /// </summary>
    [NopMigration("2026-09-28 13:00:00:0000000", "MySnacks: fix Favourites (Wishlist) being broken store-wide")]
    public class EnableProductFavouritesMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public EnableProductFavouritesMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            var maxWishlistSetting = _dataProvider.GetTable<Setting>()
                .FirstOrDefault(s => s.Name == "shoppingcartsettings.maximumwishlistitems" && s.StoreId == 0);

            if (maxWishlistSetting != null && maxWishlistSetting.Value == "0")
            {
                maxWishlistSetting.Value = "1000";
                _dataProvider.UpdateEntityAsync(maxWishlistSetting).GetAwaiter().GetResult();
            }

            var productTable = NameCompatibilityManager.GetTableName(typeof(Product));
            var disableWishlistButtonColumn = nameof(Product.DisableWishlistButton);

            IfDatabase("SqlServer").Execute.Sql(
                $"UPDATE [{productTable}] SET [{disableWishlistButtonColumn}] = 0 WHERE [{disableWishlistButtonColumn}] = 1");
            IfDatabase("Postgres").Execute.Sql(
                $"UPDATE \"{productTable}\" SET \"{disableWishlistButtonColumn}\" = false WHERE \"{disableWishlistButtonColumn}\" = true");
        }

        public override void Down()
        {
            // Data migration - no rollback (never re-disable, see class header).
        }
    }
}
