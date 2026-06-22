using FluentMigrator;
using Nop.Core.Domain.Orders;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2026-06-23 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class OrderSourceMigration : Migration
    {
        public override void Up()
        {
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Column(nameof(Order.OrderSourceId))
                    .Exists())
            {
                //add new column; existing rows stay OrderSource.Unknown (0)
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .AddColumn(nameof(Order.OrderSourceId)).AsInt32().NotNullable()
                    .WithDefaultValue((int)OrderSource.Unknown)
                    .SetExistingRowsTo((int)OrderSource.Unknown);
            }
        }

        public override void Down()
        {
            Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                .AlterColumn(nameof(Order.OrderSourceId));
        }
    }
}
