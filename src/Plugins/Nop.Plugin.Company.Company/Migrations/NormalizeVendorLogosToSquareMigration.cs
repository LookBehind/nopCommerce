using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Media;

namespace Nop.Plugin.Company.Company.Migrations
{
    /// <summary>
    /// One-time backfill: squares the logos of vendors that already exist in each tenant DB so the
    /// fix applies to current data, not just future uploads (the live save hook is
    /// VendorPictureSquareConsumer). Wide vendor logos were being cropped by width in the mobile
    /// app / storefront square tiles. Idempotent (already-square logos are skipped) and defensive
    /// (a single bad logo never blocks startup). Runs only on upgrade, not on a fresh install.
    /// </summary>
    [NopMigration("2026/06/29 12:00:00:0000000", "Company.NormalizeVendorLogosToSquare")]
    [SkipMigrationOnInstall]
    public class NormalizeVendorLogosToSquareMigration : Migration
    {
        #region Fields

        private readonly INopDataProvider _dataProvider;
        private readonly IPictureService _pictureService;

        #endregion

        #region Ctor

        public NormalizeVendorLogosToSquareMigration(INopDataProvider dataProvider, IPictureService pictureService)
        {
            _dataProvider = dataProvider;
            _pictureService = pictureService;
        }

        #endregion

        #region Methods

        public override void Up()
        {
            var vendors = _dataProvider.GetTable<Vendor>()
                .Where(v => !v.Deleted && v.PictureId > 0)
                .ToList();

            foreach (var vendor in vendors)
            {
                try
                {
                    var picture = _pictureService.GetPictureByIdAsync(vendor.PictureId).GetAwaiter().GetResult();
                    if (picture == null)
                        continue;

                    var bytes = _pictureService.LoadPictureBinaryAsync(picture).GetAwaiter().GetResult();
                    if (bytes == null || bytes.Length == 0)
                        continue;

                    var padded = VendorLogoImageHelper.PadToSquare(bytes, picture.MimeType, out var changed);
                    if (!changed)
                        continue;

                    _pictureService.UpdatePictureAsync(picture.Id, padded, picture.MimeType,
                        picture.SeoFilename, picture.AltAttribute, picture.TitleAttribute, isNew: true)
                        .GetAwaiter().GetResult();
                }
                catch
                {
                    // Never block application startup because one vendor logo failed to process.
                }
            }
        }

        public override void Down()
        {
            // No rollback: the padded originals are intentionally kept.
        }

        #endregion
    }
}
