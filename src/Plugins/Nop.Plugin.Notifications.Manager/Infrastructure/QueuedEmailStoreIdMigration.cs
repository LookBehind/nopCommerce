using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Stores;
using Nop.Data;
using Nop.Data.Mapping;
using Nop.Data.Migrations;

namespace Nop.Plugin.Notifications.Manager.Infrastructure
{
    [NopMigration("2026-01-06 00:00:00:0000001", "Notifications.Manager - Add StoreId to QueuedEmail")]
    [SkipMigrationOnInstall]
    public class QueuedEmailStoreIdMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public QueuedEmailStoreIdMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        public override void Up()
        {
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(QueuedEmail)))
                    .Column(nameof(QueuedEmail.StoreId))
                    .Exists())
            {
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(QueuedEmail)))
                    .AddColumn(nameof(QueuedEmail.StoreId)).AsInt32().NotNullable().WithDefaultValue(0);

                var singleStore = _dataProvider.GetTable<Store>().Single();
                
                Update.Table(NameCompatibilityManager.GetTableName(typeof(QueuedEmail)))
                    .Set(new {StoreId = singleStore.Id})
                    .Where(new {StoreId = 0});
            }
        }

        public override void Down()
        {
            // Rollback logic if needed
        }
    }
}
