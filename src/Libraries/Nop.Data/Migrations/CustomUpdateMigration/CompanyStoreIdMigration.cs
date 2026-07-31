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
            var companyTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Company)));
            if (companyTable.Exists() &&
                !companyTable.Column(nameof(Company.StoreId)).Exists())
            {
                Alter
                    .Table(NameCompatibilityManager.GetTableName(typeof(Company)))
                    .AddColumn(nameof(Company.StoreId))
                    .AsInt32()
                    .Nullable();

                Execute.Sql(
                    $"UPDATE [{NameCompatibilityManager.GetTableName(typeof(Company))}] " +
                    $"SET [{nameof(Company.StoreId)}] = (SELECT TOP 1 [Id] FROM [Store] ORDER BY [Id]) " +
                    $"WHERE [{nameof(Company.StoreId)}] IS NULL");
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
