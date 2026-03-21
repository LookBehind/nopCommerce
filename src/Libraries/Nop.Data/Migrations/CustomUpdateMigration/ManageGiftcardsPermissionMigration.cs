using FluentMigrator;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Security;
using Nop.Data.Mapping;
using Nop.Data.Mapping.Builders.Security;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2022-07-11 01:34:17:6453226", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class ManageGiftcardsPermissionMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public ManageGiftcardsPermissionMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            _dataProvider.InsertEntity(new PermissionRecord()
            {
                Category = "Order",
                Name = "API. Manage company gift cards on behalf of company users",
                SystemName = "ManageCompanyGiftCards"
            });

            _dataProvider.InsertEntity(new CustomerRole()
            {
                Active = true,
                Name = "Company Gift Card Manager",
                SystemName = "CompanyGiftCardManager",
                IsSystemRole = false
            });
        }

        public override void Down()
        {
        }
    }
}