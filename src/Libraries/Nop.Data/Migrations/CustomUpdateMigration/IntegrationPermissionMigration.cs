using FluentMigrator;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Security;
using Nop.Data.Mapping;
using Nop.Data.Mapping.Builders.Security;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2023-07-15 01:34:17:6453226", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class IntegrationPermissionMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public IntegrationPermissionMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            _dataProvider.InsertEntity(new PermissionRecord()
            {
                Category = "Order",
                Name = "API. Third party integration for external orders",
                SystemName = "ExternalOrdersCreation"
            });

            _dataProvider.InsertEntity(new CustomerRole()
            {
                Active = true,
                Name = "External orders role",
                SystemName = "ExternalOrdersVendor",
                IsSystemRole = false
            });
            
            // --------------------------------------------
            
            _dataProvider.InsertEntity(new PermissionRecord()
            {
                Category = "Order",
                Name = "API. Third party integration for voiding company allowance",
                SystemName = "CompanyAllowanceVoiding"
            });

            _dataProvider.InsertEntity(new CustomerRole()
            {
                Active = true,
                Name = "Company allowance voiding role",
                SystemName = "CompanyAllowanceVoider",
                IsSystemRole = false
            });
        }

        public override void Down()
        {
        }
    }
}