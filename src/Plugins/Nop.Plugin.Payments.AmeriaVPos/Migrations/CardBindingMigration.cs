using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Payments.AmeriaVPos.Domain;

namespace Nop.Plugin.Payments.AmeriaVPos.Migrations
{
    [NopMigration("2026-09-24 18:00:00:0000000", "AmeriaVPos Card Binding")]
    public class CardBindingMigration : MigrationBase
    {
        #region Fields

        private readonly IMigrationManager _migrationManager;

        #endregion

        #region Ctor

        public CardBindingMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        #endregion

        #region Methods

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CardBindingAttempt))).Exists())
                _migrationManager.BuildTable<CardBindingAttempt>(Create);

            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(CustomerBoundCard))).Exists())
                _migrationManager.BuildTable<CustomerBoundCard>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }

        #endregion
    }
}
