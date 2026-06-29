using System;
using System.Threading.Tasks;
using Nop.Core.Domain.Vendors;
using Nop.Core.Events;
using Nop.Plugin.Company.Company.Services;
using Nop.Services.Events;
using Nop.Services.Logging;
using Nop.Services.Media;

namespace Nop.Plugin.Company.Company.Infrastructure
{
    /// <summary>
    /// Normalizes a vendor's logo to a square (1:1) canvas whenever the vendor is created or
    /// updated. The mobile app and storefront render vendor logos in fixed square tiles with a
    /// "cover" fit, so wide (landscape) logos were being cropped by width. Because nopCommerce's
    /// thumbnailer preserves aspect ratio, squaring the stored original makes every generated
    /// thumbnail square as well — no client change required.
    ///
    /// Auto-discovered and registered via the IConsumer&lt;T&gt; convention (no explicit DI entry).
    /// Idempotent: already-square logos are left untouched, so repeated vendor saves are no-ops.
    /// Updating the Picture (not the Vendor) means this cannot re-trigger itself.
    /// </summary>
    public class VendorPictureSquareConsumer :
        IConsumer<EntityInsertedEvent<Vendor>>,
        IConsumer<EntityUpdatedEvent<Vendor>>
    {
        #region Fields

        private readonly IPictureService _pictureService;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public VendorPictureSquareConsumer(IPictureService pictureService, ILogger logger)
        {
            _pictureService = pictureService;
            _logger = logger;
        }

        #endregion

        #region Methods

        public Task HandleEventAsync(EntityInsertedEvent<Vendor> eventMessage)
        {
            return NormalizeVendorLogoAsync(eventMessage?.Entity);
        }

        public Task HandleEventAsync(EntityUpdatedEvent<Vendor> eventMessage)
        {
            return NormalizeVendorLogoAsync(eventMessage?.Entity);
        }

        #endregion

        #region Utilities

        private async Task NormalizeVendorLogoAsync(Vendor vendor)
        {
            if (vendor == null || vendor.PictureId <= 0)
                return;

            try
            {
                var picture = await _pictureService.GetPictureByIdAsync(vendor.PictureId);
                if (picture == null)
                    return;

                var bytes = await _pictureService.LoadPictureBinaryAsync(picture);
                if (bytes == null || bytes.Length == 0)
                    return;

                var padded = VendorLogoImageHelper.PadToSquare(bytes, picture.MimeType, out var changed);
                if (!changed)
                    return;

                // UpdatePictureAsync clears the cached thumbnails, so square thumbs regenerate on demand.
                await _pictureService.UpdatePictureAsync(picture.Id, padded, picture.MimeType,
                    picture.SeoFilename, picture.AltAttribute, picture.TitleAttribute, isNew: true);

                await _logger.InformationAsync(
                    $"Vendor logo squared (vendorId={vendor.Id}, pictureId={picture.Id}).");
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync(
                    $"Failed to square vendor logo (vendorId={vendor.Id}, pictureId={vendor.PictureId}): {ex.Message}", ex);
            }
        }

        #endregion
    }
}
