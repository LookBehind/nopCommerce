using FluentMigrator;
using Nop.Core.Domain.Orders;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2022-05-29 08:19:17:6453226", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class OrderCompanyIdMigration : Migration
    {
        public override void Up()
        {
            
        }

        public override void Down()
        {
            Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                .AlterColumn(nameof(Order.CompanyId));
        }
    }
}