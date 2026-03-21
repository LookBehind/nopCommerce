using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Messages;

namespace Nop.Data.Migrations.UpgradeTo440
{
    [NopMigration("2020-11-10 06:30:17:6445795", "4.40.0", UpdateMigrationType.Data)]
    public class CancellationMessageTemplateMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public CancellationMessageTemplateMigration(INopDataProvider dataProvider)
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
