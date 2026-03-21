using System;
using System.Linq;
using FluentMigrator;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tasks;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    [NopMigration("2024-06-02 09:30:17:6453226", "4.40.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class ReminderNamespaceMigration : Migration
    {
        private readonly INopDataProvider _dataProvider;

        public ReminderNamespaceMigration(INopDataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        /// <summary>
        /// Collect the UP migration expressions
        /// </summary>
        public override void Up()
        {
            var scheduleTaskTable = _dataProvider.GetTable<ScheduleTask>();

            scheduleTaskTable.UpdateAsync(st => string.Equals(st.Name, "Remind Me Notification Task", StringComparison.OrdinalIgnoreCase),
                st => new ScheduleTask()
                {
                    Type = "Nop.Plugin.Notifications.Manager.ScheduledTasks.RemindMeNotificationTask",
                    StopOnError = false,
                    Id = st.Id,
                    LastStartUtc = st.LastStartUtc,
                    LastSuccessUtc = st.LastSuccessUtc,
                    Enabled = st.Enabled,
                    Seconds = st.Seconds,
                    LastEndUtc = st.LastEndUtc,
                    Name = st.Name,
                });
            
            scheduleTaskTable.UpdateAsync(st => string.Equals(st.Name, "Rate Reminder Notification Task", StringComparison.OrdinalIgnoreCase),
                st => new ScheduleTask()
                {
                    Type = "Nop.Plugin.Notifications.Manager.ScheduledTasks.RateRemainderNotificationTask",
                    StopOnError = false,
                    Id = st.Id,
                    LastStartUtc = st.LastStartUtc,
                    LastSuccessUtc = st.LastSuccessUtc,
                    Enabled = st.Enabled,
                    Seconds = st.Seconds,
                    LastEndUtc = st.LastEndUtc,
                    Name = st.Name,
                });
        }

        public override void Down()
        {
            //add the downgrade logic if necessary 
        }
    }
}
