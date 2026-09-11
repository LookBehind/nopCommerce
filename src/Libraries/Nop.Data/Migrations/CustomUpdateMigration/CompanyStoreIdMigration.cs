using FluentMigrator;
using Nop.Core.Domain.Companies;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Adds a real StoreId FK to Company (previously the Store<->Company relationship was only an
    /// informal one-store-per-tenant convention, not enforced anywhere in the schema). Backfills
    /// existing rows to the tenant's own store via a subquery (not a hardcoded id, since a store's id
    /// isn't guaranteed to be the same across every tenant DB).
    /// </summary>
    [NopMigration("2026-07-31 00:00:01:0000000", "Company - add StoreId FK")]
    [SkipMigrationOnInstall]
    public class CompanyStoreIdMigration : Migration
    {
        public override void Up()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(Company));
            var storeIdColumn = nameof(Company.StoreId);
            var companyTable = Schema.Table(tableName);

            if (companyTable.Exists() &&
                !companyTable.Column(storeIdColumn).Exists())
            {
                Alter
                    .Table(tableName)
                    .AddColumn(storeIdColumn)
                    .AsInt32()
                    .Nullable();

                // Backfill each existing company to the tenant's first store (lowest store id).
                // The backfill SQL must be provider-specific because identifier quoting differs
                // per dialect (SQL Server [ ], PostgreSQL " ", MySQL ` `). The previous single
                // SQL Server-only string (bracket quoting + TOP) threw "syntax error at or near
                // '['" on PostgreSQL-backed installs (local dev), crashing startup. MIN([Id])
                // replaces "TOP 1 ... ORDER BY [Id]" so only the quoting differs per branch.
                IfDatabase("SqlServer").Execute.Sql(
                    $"UPDATE [{tableName}] SET [{storeIdColumn}] = (SELECT MIN([Id]) FROM [Store]) WHERE [{storeIdColumn}] IS NULL");
                IfDatabase("Postgres").Execute.Sql(
                    $"UPDATE \"{tableName}\" SET \"{storeIdColumn}\" = (SELECT MIN(\"Id\") FROM \"Store\") WHERE \"{storeIdColumn}\" IS NULL");
                IfDatabase("MySql").Execute.Sql(
                    $"UPDATE `{tableName}` SET `{storeIdColumn}` = (SELECT MIN(`Id`) FROM `Store`) WHERE `{storeIdColumn}` IS NULL");
            }
        }

        public override void Down()
        {
            var companyTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Company)));
            if (companyTable.Exists() &&
                companyTable.Column(nameof(Company.StoreId)).Exists())
            {
                Delete
                    .Column(nameof(Company.StoreId))
                    .FromTable(NameCompatibilityManager.GetTableName(typeof(Company)));
            }
        }
    }
}
