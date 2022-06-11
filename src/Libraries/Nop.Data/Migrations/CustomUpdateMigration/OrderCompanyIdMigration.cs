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
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(Order))).Column(nameof(Order.CompanyId)).Exists())
            {
                //add new column
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                    .AddColumn(nameof(Order.CompanyId)).AsInt32().Nullable();
            }
        }

        public override void Down()
        {
            Alter.Table(NameCompatibilityManager.GetTableName(typeof(Order)))
                .AlterColumn(nameof(Order.CompanyId));
        }
    }
}