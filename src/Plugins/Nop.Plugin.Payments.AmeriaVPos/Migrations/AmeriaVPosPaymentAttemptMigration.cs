using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Payments.AmeriaVPos.Domain;

namespace Nop.Plugin.Payments.AmeriaVPos.Migrations
{
    [NopMigration("2026/07/21 00:00:00:0000000", "AmeriaVPos Payment Attempt")]
    public class AmeriaVPosPaymentAttemptMigration : MigrationBase
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public AmeriaVPosPaymentAttemptMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Collect the UP migration expressions
        /// </summary>
        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(AmeriaVPosPaymentAttempt))).Exists())
                _migrationManager.BuildTable<AmeriaVPosPaymentAttempt>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }

        #endregion
    }
}
