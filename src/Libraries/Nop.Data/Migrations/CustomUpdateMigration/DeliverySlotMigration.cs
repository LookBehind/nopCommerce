using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Security;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{

    [NopMigration("2022-05-21 08:19:17:6453226", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class DeliverySlotMigration : Migration
    {

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(Order))).Column(nameof(Order.DeliverySlot)).Exists())
            {
                //add new column
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .AddColumn(nameof(Order.DeliverySlot)).AsInt32().Nullable()
                    .AddColumn(nameof(Order.ScheduleDateTime)).AsDateTime()
                    .WithDefaultValue(DateTime.UtcNow);
                Execute.Sql("update [mysnacks].[dbo].[Order] set DeliverySlot = Id where DeliverySlot is null");
            }
        }

        public override void Down()
        {
            Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                .AlterColumn(nameof(Order.DeliverySlot));
        }
    }
}
