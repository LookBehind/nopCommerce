using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Company.Support.Domain;

namespace Nop.Plugin.Company.Support.Migrations
{
    /// <summary>
    /// Adds the read-tracking columns (customer/staff last-read + last-message timestamps)
    /// used to compute unread badges on both the mobile app and the admin queue.
    /// </summary>
    [NopMigration("2026/09/26 12:00:00:0000000", "Company.Support.ReadTracking")]
    public class SupportCaseReadTrackingMigration : Migration
    {
        public override void Up()
        {
            var tableName = NameCompatibilityManager.GetTableName(typeof(SupportCase));

            AddNullableDateTimeColumn(tableName, nameof(SupportCase.CustomerLastReadUtc));
            AddNullableDateTimeColumn(tableName, nameof(SupportCase.StaffLastReadUtc));
            AddNullableDateTimeColumn(tableName, nameof(SupportCase.LastCustomerMessageUtc));
            AddNullableDateTimeColumn(tableName, nameof(SupportCase.LastStaffMessageUtc));
        }

        private void AddNullableDateTimeColumn(string tableName, string columnName)
        {
            if (!Schema.Table(tableName).Column(columnName).Exists())
            {
                Alter.Table(tableName)
                    .AddColumn(columnName).AsDateTime2().Nullable().SetExistingRowsTo(null);
            }
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
