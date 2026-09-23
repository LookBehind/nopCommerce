using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Nop.Core.Configuration;

namespace Nop.Services.Media
{
    /// <summary>
    /// <see cref="IMediaBlobStorageProvider"/> implementation for Azure Blob
    /// Storage. Extracted from the old AzurePictureService (see git history) -
    /// same lazy static-init pattern (one BlobContainerClient per process,
    /// guarded by a lock, regardless of how many scoped instances DI creates),
    /// same behavior, just split out so CloudPictureService can use either this
    /// or S3BlobStorageProvider interchangeably.
    /// </summary>
    public partial class AzureBlobStorageProvider : IMediaBlobStorageProvider
    {
        #region Fields

        private static BlobContainerClient _blobContainerClient;
        private static BlobServiceClient _blobServiceClient;
        private static bool _appendContainerName;
        private static bool _isInitialized;
        private static string _containerName;
        private static string _endPoint;

        private static readonly object _locker = new();

        #endregion

        #region Ctor

        public AzureBlobStorageProvider(AppSettings appSettings)
        {
            OneTimeInit(appSettings);
        }

        #endregion

        #region Utilities

        protected static void OneTimeInit(AppSettings appSettings)
        {
            if (_isInitialized)
                return;

            var config = appSettings.AzureBlobConfig;

            if (string.IsNullOrEmpty(config.ConnectionString))
                throw new Exception("Azure connection string for Blob is not specified");

            if (string.IsNullOrEmpty(config.ContainerName))
                throw new Exception("Azure container name for Blob is not specified");

            if (string.IsNullOrEmpty(config.EndPoint))
                throw new Exception("Azure end point for Blob is not specified");

            lock (_locker)
            {
                if (_isInitialized)
                    return;

                _appendContainerName = config.AppendContainerName;
                _containerName = config.ContainerName.Trim().ToLower();
                _endPoint = config.EndPoint.Trim().ToLower().TrimEnd('/');

                _blobServiceClient = new BlobServiceClient(config.ConnectionString);
                _blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                _blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.Blob).GetAwaiter().GetResult();

                _isInitialized = true;
            }
        }

        #endregion

        #region Methods

        public string GetPublicUrl(string fileName)
        {
            var path = _appendContainerName ? $"{_containerName}/" : string.Empty;
            return $"{_endPoint}/{path}{fileName}";
        }

        public async Task<bool> ExistsAsync(string fileName)
        {
            return await _blobContainerClient.GetBlobClient(fileName).ExistsAsync();
        }

        public async Task UploadAsync(string fileName, byte[] binary, string mimeType, string cacheControl)
        {
            var blobClient = _blobContainerClient.GetBlobClient(fileName);
            await using var ms = new MemoryStream(binary);

            BlobHttpHeaders headers = null;
            if (!string.IsNullOrWhiteSpace(mimeType))
                headers = new BlobHttpHeaders { ContentType = mimeType };

            if (!string.IsNullOrWhiteSpace(cacheControl))
            {
                headers ??= new BlobHttpHeaders();
                headers.CacheControl = cacheControl;
            }

            if (headers is null)
                await blobClient.UploadAsync(ms);
            else
                await blobClient.UploadAsync(ms, new BlobUploadOptions { HttpHeaders = headers });
        }

        public async Task DeleteByPrefixAsync(string prefix)
        {
            var tasks = new List<Task>();
            await foreach (var blob in _blobContainerClient.GetBlobsAsync(BlobTraits.All, BlobStates.All, prefix))
            {
                tasks.Add(_blobContainerClient.DeleteBlobIfExistsAsync(blob.Name, DeleteSnapshotsOption.IncludeSnapshots));
            }
            await Task.WhenAll(tasks);
        }

        #endregion
    }
}
