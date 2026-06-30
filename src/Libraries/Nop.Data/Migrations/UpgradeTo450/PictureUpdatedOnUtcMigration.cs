using FluentMigrator;
using Nop.Core.Domain.Media;
using Nop.Data.Mapping;

namespace Nop.Data.Migrations.UpgradeTo450
{
    /// <summary>
    /// Adds Picture.UpdatedOnUtc (nullable) so thumbnail URLs can be versioned by content. Existing rows
    /// stay NULL — they keep their current (un-versioned) thumbnail filenames, so no images are
    /// regenerated or cache-busted on deploy. Only pictures whose binary changes afterwards get a token.
    /// Timestamp is intentionally earlier than the Company plugin's vendor-logo backfill migration
    /// (2026/06/29 12:00) so the column exists before that migration bumps it.
    /// </summary>
    [NopMigration("2026/06/28 09:00:00:0000000", "Picture.UpdatedOnUtc")]
    [SkipMigrationOnInstall]
    public class PictureUpdatedOnUtcMigration : Migration
    {
        public override void Up()
        {
            var pictureTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Picture)));
            if (pictureTable.Exists() &&
                !pictureTable.Column(nameof(Picture.UpdatedOnUtc)).Exists())
            {
                Alter
                    .Table(NameCompatibilityManager.GetTableName(typeof(Picture)))
                    .AddColumn(nameof(Picture.UpdatedOnUtc))
                    .AsDateTime2()
                    .Nullable();
            }
        }

        public override void Down()
        {
            var pictureTable = Schema.Table(NameCompatibilityManager.GetTableName(typeof(Picture)));
            if (pictureTable.Exists() &&
                pictureTable.Column(nameof(Picture.UpdatedOnUtc)).Exists())
            {
                Delete
                    .Column(nameof(Picture.UpdatedOnUtc))
                    .FromTable(NameCompatibilityManager.GetTableName(typeof(Picture)));
            }
        }
    }
}
