using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Messages;

namespace Nop.Data.Migrations.UpgradeTo450
{
    [NopMigration("2021-04-23 00:00:00", "4.50.0", UpdateMigrationType.Data)]
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
            var record = _dataProvider.GetTable<MessageTemplate>().Where(e => e.Name == "OrderCancelled.VendorNotification").FirstOrDefault();
            if (record == null)
            {
                _dataProvider.InsertEntity(new MessageTemplate()
                {
                    Name = "OrderCancelled.VendorNotification",
                    Subject = "%Store.Name%. Order Cancelled",
                    Body = "%Customer.FullName% has just cancelled the order." + Environment.NewLine +
                            "Order #: %Order.OrderNumber%" + Environment.NewLine +
                            "Shipping Address: %Order.ShippingAddress1%, %Order.ShippingAddress2%" + Environment.NewLine +
                            "%Order.ProductsHumanReadable%",
                    IsActive = true,
                });
            }
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }
    }
}
