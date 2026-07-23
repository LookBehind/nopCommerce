using FluentMigrator;
using Nop.Core.Domain.Customers;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.CustomUpdateMigration
{
    /// <summary>
    /// Adds the nullable <see cref="Customer.RemindMeTime"/> column (per-customer order-reminder time, minutes
    /// after local midnight) to existing installs. Fresh installs get the column from the entity schema, so it
    /// is skipped on install. Idempotent. See docs/plans/2026-07-22-dynamic-scheduled-tasks.md.
    /// </summary>
    [NopMigration("2026-07-22 00:00:02:0000000", "4.60.0", UpdateMigrationType.Data)]
    [SkipMigrationOnInstall]
    public class CustomerRemindMeTimeMigration : Migration
    {
        public override void Up()
        {
            if (!Schema
                    .Table(NameCompatibilityManager.GetTableName(typeof(Customer)))
                    .Column(nameof(Customer.RemindMeTime))
                    .Exists())
            {
                Alter.Table(NameCompatibilityManager.GetTableName(typeof(Customer)))
                    .AddColumn(nameof(Customer.RemindMeTime)).AsInt32().Nullable();
            }
        }

        public override void Down()
        {
            // no rollback
        }
    }
}
