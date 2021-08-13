using FluentMigrator;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.UpgradeTo450
{
    [NopMigration("2021-04-23 00:00:02", "4.50.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class DataMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public DataMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        /// <summary>
        /// Collect the UP migration expressions
        /// </summary>
        public override void Up()
        {
            var ordersTable = NameCompatibilityManager.GetTableName(typeof(Order));
            //add column
            if (!Schema.Table(ordersTable).Column(nameof(Order.IsFavorite)).Exists())
            {
                Alter.Table(ordersTable)
                    .AddColumn(nameof(Order.IsFavorite)).AsBoolean().NotNullable().SetExistingRowsTo(0).WithDefaultValue(0);
            }
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }
    }
}
