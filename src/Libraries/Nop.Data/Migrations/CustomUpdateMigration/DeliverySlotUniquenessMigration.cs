using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;
using Nop.Core.Domain.Orders;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2022-05-21 11:30:17:6453226", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class DeliverySlotUniquenessMigration : Migration
    {
        public override void Up()
        {
            if (Schema.Table(NameCompatibilityManager.GetTableName(typeof(Order))).Column(nameof(Order.DeliverySlot)).Exists()
                &&
                !Schema.Table(NameCompatibilityManager.GetTableName(typeof(Order))).Index("DeliverSlotUnique").Exists())
            {
                Create.UniqueConstraint("DeliverSlotUnique").OnTable(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Columns(nameof(Order.ScheduleDateTime), nameof(Order.DeliverySlot));
            }
        }
        public override void Down()
        {

        }
    }
}