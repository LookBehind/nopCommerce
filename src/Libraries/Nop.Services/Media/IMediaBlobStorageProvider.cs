using System.Threading.Tasks;

namespace Nop.Services.Media
{
    /// <summary>
    /// Abstraction over a cloud blob storage backend used for generated picture
    /// thumbnails (see <see cref="CloudPictureService"/>). Implemented by
    /// <see cref="AzureBlobStorageProvider"/> and <see cref="S3BlobStorageProvider"/>
    /// (any S3-compatible endpoint, e.g. a self-hosted Garage cluster - not just
    /// AWS) so CloudPictureService's caching/orchestration logic doesn't need to
    /// know which backend is actually configured. Caching (NopMediaDefaults.
    /// ThumbExistsCacheKey/ThumbsExistsPrefix) stays in CloudPictureService, not
    /// here - implementations only do raw storage I/O.
    /// </summary>
    public interface IMediaBlobStorageProvider
    {
        /// <returns>True if a blob with this name already exists.</returns>
        Task<bool> ExistsAsync(string fileName);

        /// <summary>Uploads (or overwrites) a blob.</summary>
        /// <param name="cacheControl">Optional Cache-Control header value; null/empty to omit.</param>
        Task UploadAsync(string fileName, byte[] binary, string mimeType, string cacheControl);

        /// <summary>Deletes every blob whose name starts with <paramref name="prefix"/>.</summary>
        Task DeleteByPrefixAsync(string prefix);

        /// <summary>The public, browser-reachable URL for a blob by name.</summary>
        string GetPublicUrl(string fileName);
    }
}
