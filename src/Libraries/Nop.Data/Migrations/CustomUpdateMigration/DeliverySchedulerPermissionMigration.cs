using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Security;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2022-04-26 09:30:17:6453226", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class DeliverySchedulerPermissionMigration : Migration
    {

        private readonly INopDataProvider _dataProvider;

        public DeliverySchedulerPermissionMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }
        public override void Up()
        {
            //add manage delivery scheduler
            if (!_dataProvider.GetTable<PermissionRecord>().Any(pr => string.Compare(pr.SystemName, "ManageDeliveryScheduler", true) == 0))
            {
                var manageDeliveryScheduler = _dataProvider.InsertEntity(
                    new PermissionRecord
                    {
                        Name = "Admin area. Manage Delivery Scheduler",
                        SystemName = "ManageDeliveryScheduler",
                        Category = "Order"
                    }
                );

                //add it to the Admin role by default
                var adminRole = _dataProvider
                    .GetTable<CustomerRole>()
                    .FirstOrDefault(x => x.IsSystemRole && x.SystemName == NopCustomerDefaults.AdministratorsRoleName);

                _dataProvider.InsertEntity(
                    new PermissionRecordCustomerRoleMapping
                    {
                        CustomerRoleId = adminRole.Id,
                        PermissionRecordId = manageDeliveryScheduler.Id
                    }
                );
            }
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }
    }
}
