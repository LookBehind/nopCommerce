using System;
using Nop.Core.Infrastructure;
using Nop.Services.Media;
using SkiaSharp;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Pads vendor logo images to a 1:1 (square) canvas so that the mobile app and storefront,
    /// which render vendor logos in fixed square tiles with a "cover" fit, no longer crop wide
    /// (landscape) logos. nopCommerce's thumbnailer preserves aspect ratio, so once the stored
    /// original is square every generated thumbnail is square too.
    ///
    /// Pure/stateless on purpose: shared by both the live save hook (VendorPictureSquareConsumer)
    /// and the one-time backfill migration (NormalizeVendorLogosToSquareMigration).
    /// </summary>
    public static class VendorLogoImageHelper
    {
        /// <summary>
        /// Logos whose shorter side is at least this fraction of the longer side are treated as
        /// already-square and left untouched (keeps the operation idempotent and avoids re-encoding
        /// near-square logos for sub-pixel differences, e.g. 1170x1166).
        /// </summary>
        private const double SQUARE_TOLERANCE = 0.98;

        /// <summary>JPEG encode quality for opaque logos.</summary>
        private const int ENCODE_QUALITY = 90;

        /// <summary>
        /// Returns a square-padded copy of <paramref name="data"/>, or the original bytes unchanged
        /// when the image is already (near-)square or cannot be decoded.
        /// PNG/WebP/GIF logos are padded with transparency (so they sit naturally on the card
        /// background); opaque formats (JPEG/BMP) are padded with white.
        /// </summary>
        /// <param name="data">Original image bytes</param>
        /// <param name="mimeType">Picture MIME type (e.g. image/png, image/jpeg)</param>
        /// <param name="changed">True when a new, padded image was produced</param>
        /// <returns>Padded image bytes, or the original bytes when no change was needed</returns>
        public static byte[] PadToSquare(byte[] data, string mimeType, out bool changed)
        {
            changed = false;

            if (data == null || data.Length == 0)
                return data;

            using var input = SKBitmap.Decode(data);
            if (input == null || input.Width <= 0 || input.Height <= 0)
                return data;

            var w = input.Width;
            var h = input.Height;
            if (w == h)
                return data;

            var shorter = Math.Min(w, h);
            var longer = Math.Max(w, h);
            if ((double)shorter / longer >= SQUARE_TOLERANCE)
                return data;

            var supportsAlpha = MimeTypeSupportsAlpha(mimeType);
            var side = longer;

            var info = new SKImageInfo(side, side, SKColorType.Rgba8888,
                supportsAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque);

            using var surface = SKSurface.Create(info);
            if (surface == null)
                return data;

            var canvas = surface.Canvas;
            canvas.Clear(supportsAlpha ? SKColors.Transparent : SKColors.White);

            // center the original on the square canvas
            var left = (side - w) / 2f;
            var top = (side - h) / 2f;
            canvas.DrawBitmap(input, left, top);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var encoded = image.Encode(GetEncodedFormat(mimeType, supportsAlpha), ENCODE_QUALITY);
            if (encoded == null)
                return data;

            changed = true;
            return encoded.ToArray();
        }

        /// <summary>
        /// Deletes all cached thumbnails for a picture so they are regenerated from the (now square)
        /// original on the next render. Mirrors PictureService.DeletePictureThumbsAsync (same filter
        /// and recursive search) because that method is not exposed on IPictureService. Necessary
        /// because nopCommerce only regenerates a thumb when the file is ABSENT — a stale or 0-byte
        /// thumb left on disk would otherwise be served forever, and the IsNew flag alone does not
        /// reliably clear them.
        /// </summary>
        public static void DeleteThumbnails(INopFileProvider fileProvider, int pictureId)
        {
            if (fileProvider == null)
                return;

            var filter = $"{pictureId:0000000}*.*";
            var thumbsPath = fileProvider.GetAbsolutePath(NopMediaDefaults.ImageThumbsPath);

            // topDirectoryOnly: false — matches PictureService and covers MultipleThumbDirectories subfolders.
            foreach (var file in fileProvider.GetFiles(thumbsPath, filter, false))
                fileProvider.DeleteFile(file);
        }

        private static bool MimeTypeSupportsAlpha(string mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
                return false;

            var subtype = LastPart(mimeType);
            return subtype switch
            {
                "png" or "x-png" or "webp" or "gif" => true,
                _ => false
            };
        }

        private static SKEncodedImageFormat GetEncodedFormat(string mimeType, bool supportsAlpha)
        {
            var subtype = LastPart(mimeType);
            return subtype switch
            {
                "webp" => SKEncodedImageFormat.Webp,
                "png" or "x-png" or "gif" or "bmp" or "x-icon" => SKEncodedImageFormat.Png,
                // opaque logos padded with white stay JPEG; alpha-bearing ones fall back to PNG
                _ => supportsAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg
            };
        }

        private static string LastPart(string mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
                return string.Empty;

            var parts = mimeType.ToLowerInvariant().Split('/');
            return parts[^1];
        }
    }
}
