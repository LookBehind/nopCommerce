using FluentMigrator;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Company.AiChat.Domain;

namespace Nop.Plugin.Company.AiChat.Migrations
{
    /// <summary>
    /// Creates the tables backing AI Assistant conversations and their messages.
    /// </summary>
    [NopMigration("2026/09/27 15:00:00:0000000", "Company.AiChat.Tables")]
    public class AiChatMigration : Migration
    {
        private readonly IMigrationManager _migrationManager;

        public AiChatMigration(IMigrationManager migrationManager)
        {
            _migrationManager = migrationManager;
        }

        public override void Up()
        {
            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(AiChatConversation))).Exists())
                _migrationManager.BuildTable<AiChatConversation>(Create);

            if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(AiChatMessage))).Exists())
                _migrationManager.BuildTable<AiChatMessage>(Create);
        }

        public override void Down()
        {
            //add the downgrade logic if necessary
        }
    }
}
