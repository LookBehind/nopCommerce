using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;
using Nop.Core.Domain.Companies;
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
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Column(nameof(Order.DeliverySlot))
                    .Exists())
            {
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .AddColumn(nameof(Order.DeliverySlot)).AsInt32().Nullable();
                
                Execute.Sql($"update [{NameCompatibilityManager.GetTableName(typeof(Order))}] " +
                            $"set [{nameof(Order.DeliverySlot)}] = [{nameof(Order.Id)}] " + 
                            $"where [{nameof(Order.DeliverySlot)}] is null");
            }

            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Column(nameof(Order.ScheduleDateTime))
                    .Exists())
            {
                //add new column
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .AddColumn(nameof(Order.ScheduleDateTime)).AsDateTime2()
                    .WithDefaultValue(DateTime.UtcNow)
                    .SetExistingRowsTo(DateTime.UtcNow);
                
                Execute.Sql($"update [{NameCompatibilityManager.GetTableName(typeof(Order))}] " + 
                    $"set [{nameof(Order.ScheduleDateTime)}] = CAST({nameof(Order.ScheduleDate)} AS DATETIME2)");
            }
            
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Column(nameof(Order.CompanyId))
                    .Exists())
            {
                //add new column
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .AddColumn(nameof(Order.CompanyId)).AsInt32().Nullable();
                
                Execute.Sql($"update o " +
                            $"set [{nameof(Order.CompanyId)}] = [ccm].[{nameof(CompanyCustomer.CompanyId)}] " +
                            $"from [{NameCompatibilityManager.GetTableName(typeof(Order))}] o " +
                            $"join [{NameCompatibilityManager.GetTableName(typeof(CompanyCustomer))}] ccm on [ccm].[{nameof(CompanyCustomer.CustomerId)}] = o.[{nameof(Order.CustomerId)}] ");
            }
            
            if (Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Column(nameof(Order.DeliverySlot))
                    .Exists()
                &&
                !Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Index("DeliverSlotUnique")
                    .Exists())
            {
                Create
                    .UniqueConstraint("DeliverSlotUnique")
                    .OnTable(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .Columns(nameof(Order.ScheduleDateTime), 
                        nameof(Order.DeliverySlot), 
                        nameof(Order.CompanyId));
            }
        }

        public override void Down()
        {
            Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                .AlterColumn(nameof(Order.DeliverySlot));
        }
    }
}
