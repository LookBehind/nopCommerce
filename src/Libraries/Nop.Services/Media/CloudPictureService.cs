using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Seo;

namespace Nop.Services.Media
{
    /// <summary>
    /// Picture service that offloads generated thumbnails to whichever cloud
    /// blob storage backend is configured (<see cref="AzureBlobStorageProvider"/>
    /// or <see cref="S3BlobStorageProvider"/> - S3-compatible, including a
    /// self-hosted Garage/MinIO cluster) via <see cref="IMediaBlobStorageProvider"/>.
    /// Replaces the old backend-specific AzurePictureService (see git history) -
    /// same behavior for Azure, generalized so a second backend doesn't need a
    /// second near-duplicate PictureService subclass. Caching/invalidation
    /// (NopMediaDefaults.ThumbExistsCacheKey/ThumbsExistsPrefix) lives here,
    /// not in the provider - providers only do raw storage I/O.
    ///
    /// Like the old AzurePictureService, this only offloads generated
    /// thumbnails (the 5 protected hooks PictureService exposes for that) -
    /// original/full-size images still go through the base class's own
    /// disk/DB storage (MediaSettings.StoreInDb).
    /// </summary>
    public partial class CloudPictureService : PictureService
    {
        #region Fields

        private readonly IMediaBlobStorageProvider _blobStorageProvider;
        private readonly IStaticCacheManager _staticCacheManager;
        private readonly MediaSettings _mediaSettings;

        #endregion

        #region Ctor

        public CloudPictureService(IMediaBlobStorageProvider blobStorageProvider,
            INopDataProvider dataProvider,
            IDownloadService downloadService,
            IHttpContextAccessor httpContextAccessor,
            INopFileProvider fileProvider,
            IProductAttributeParser productAttributeParser,
            IRepository<Picture> pictureRepository,
            IRepository<PictureBinary> pictureBinaryRepository,
            IRepository<ProductPicture> productPictureRepository,
            ISettingService settingService,
            IStaticCacheManager staticCacheManager,
            IUrlRecordService urlRecordService,
            IWebHelper webHelper,
            MediaSettings mediaSettings)
            : base(dataProvider,
                  downloadService,
                  httpContextAccessor,
                  fileProvider,
                  productAttributeParser,
                  pictureRepository,
                  pictureBinaryRepository,
                  productPictureRepository,
                  settingService,
                  urlRecordService,
                  webHelper,
                  mediaSettings)
        {
            _blobStorageProvider = blobStorageProvider;
            _staticCacheManager = staticCacheManager;
            _mediaSettings = mediaSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Get picture (thumb) local path
        /// </summary>
        /// <param name="thumbFileName">Filename</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the local picture thumb path
        /// </returns>
        protected override Task<string> GetThumbLocalPathAsync(string thumbFileName)
        {
            return Task.FromResult(_blobStorageProvider.GetPublicUrl(thumbFileName));
        }

        /// <summary>
        /// Get picture (thumb) URL
        /// </summary>
        /// <param name="thumbFileName">Filename</param>
        /// <param name="storeLocation">Store location URL; null to use determine the current store location automatically</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the local picture thumb path
        /// </returns>
        protected override async Task<string> GetThumbUrlAsync(string thumbFileName, string storeLocation = null)
        {
            return await GetThumbLocalPathAsync(thumbFileName);
        }

        /// <summary>
        /// Initiates an asynchronous operation to delete picture thumbs
        /// </summary>
        /// <param name="picture">Picture</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        protected override async Task DeletePictureThumbsAsync(Picture picture)
        {
            var prefix = $"{picture.Id:0000000}";

            await _blobStorageProvider.DeleteByPrefixAsync(prefix);

            await _staticCacheManager.RemoveByPrefixAsync(NopMediaDefaults.ThumbsExistsPrefix);
        }

        /// <summary>
        /// Initiates an asynchronous operation to get a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFilePath">Thumb file path</param>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        protected override async Task<bool> GeneratedThumbExistsAsync(string thumbFilePath, string thumbFileName)
        {
            try
            {
                var key = _staticCacheManager.PrepareKeyForDefaultCache(NopMediaDefaults.ThumbExistsCacheKey, thumbFileName);

                return await _staticCacheManager.GetAsync(key, () => _blobStorageProvider.ExistsAsync(thumbFileName));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initiates an asynchronous operation to save a value indicating whether some file (thumb) already exists
        /// </summary>
        /// <param name="thumbFilePath">Thumb file path</param>
        /// <param name="thumbFileName">Thumb file name</param>
        /// <param name="mimeType">MIME type</param>
        /// <param name="binary">Picture binary</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        protected override async Task SaveThumbAsync(string thumbFilePath, string thumbFileName, string mimeType, byte[] binary)
        {
            // Reused across both backends despite the Azure-specific setting name -
            // renaming it would need a Setting-table migration for one cosmetic
            // rename, not worth it for this pass; it's just "the Cache-Control
            // header value to set on uploaded thumbnails," backend-agnostic.
            await _blobStorageProvider.UploadAsync(thumbFileName, binary, mimeType, _mediaSettings.AzureCacheControlHeader);

            await _staticCacheManager.RemoveByPrefixAsync(NopMediaDefaults.ThumbsExistsPrefix);
        }

        #endregion
    }
}
