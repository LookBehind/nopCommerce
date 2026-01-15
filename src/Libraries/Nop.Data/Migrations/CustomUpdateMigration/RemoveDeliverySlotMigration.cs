using FluentMigrator;
using Nop.Core.Domain.Orders;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2026-01-16 00:00:00:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class RemoveDeliverySlotMigration : Migration
    {
        public override void Up()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(Order));

            // Drop the unique constraint if it exists
            if (Schema
                    .Table(tableName)
                    .Index("DeliverSlotUnique")
                    .Exists())
            {
                Delete
                    .UniqueConstraint("DeliverSlotUnique")
                    .FromTable(tableName);
            }

            // Drop the DeliverySlot column if it exists
            if (Schema
                    .Table(tableName)
                    .Column("DeliverySlot")
                    .Exists())
            {
                Delete
                    .Column("DeliverySlot")
                    .FromTable(tableName);
            }
        }

        public override void Down()
        {
            // Never recreate it back
        }
    }
}
