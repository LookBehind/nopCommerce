using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Nop.Core.Configuration;

namespace Nop.Services.Media
{
    /// <summary>
    /// <see cref="IMediaBlobStorageProvider"/> implementation for any
    /// S3-compatible object store - real AWS S3, or a self-hosted cluster such
    /// as Garage/MinIO (this org already runs a Garage cluster for DB backups
    /// and the Harbor image registry - see S3Config.ServiceUrl/Region). Same
    /// lazy static-init shape as <see cref="AzureBlobStorageProvider"/>.
    ///
    /// Unlike Azure's container creation (CreateIfNotExistsAsync with public
    /// blob access, done inline by AzureBlobStorageProvider), the bucket here
    /// is expected to already exist and be provisioned for public read
    /// out-of-band - Garage's public-access flag is a Garage-CLI-only concept
    /// (`garage bucket website`/permission grants), not something reachable
    /// through the S3 API itself, and a real AWS bucket's public-read policy
    /// is a deliberate, separate admin action too (modern AWS disables
    /// per-object ACLs by default). Provisioning buckets is already how this
    /// org manages Garage (see mysnacks-backups/harbor-registry), so this
    /// mirrors that rather than trying to auto-create+expose a bucket through
    /// an API that can't fully express it.
    /// </summary>
    public partial class S3BlobStorageProvider : IMediaBlobStorageProvider
    {
        #region Fields

        private static IAmazonS3 _client;
        private static bool _isInitialized;
        private static string _bucketName;
        private static bool _appendBucketName;
        private static string _publicBaseUrl;

        private static readonly object _locker = new();

        #endregion

        #region Ctor

        public S3BlobStorageProvider(AppSettings appSettings)
        {
            OneTimeInit(appSettings);
        }

        #endregion

        #region Utilities

        protected static void OneTimeInit(AppSettings appSettings)
        {
            if (_isInitialized)
                return;

            var config = appSettings.S3Config;

            if (string.IsNullOrEmpty(config.BucketName))
                throw new Exception("S3 bucket name is not specified");

            if (string.IsNullOrEmpty(config.AccessKey) || string.IsNullOrEmpty(config.SecretKey))
                throw new Exception("S3 access/secret key is not specified");

            lock (_locker)
            {
                if (_isInitialized)
                    return;

                _bucketName = config.BucketName.Trim().ToLower();
                _appendBucketName = config.AppendBucketName;
                _publicBaseUrl = (!string.IsNullOrEmpty(config.PublicBaseUrl) ? config.PublicBaseUrl : config.ServiceUrl)
                    ?.Trim().TrimEnd('/');

                var clientConfig = new AmazonS3Config
                {
                    ForcePathStyle = config.ForcePathStyle
                };

                if (!string.IsNullOrEmpty(config.ServiceUrl))
                    clientConfig.ServiceURL = config.ServiceUrl;

                if (!string.IsNullOrEmpty(config.Region))
                    clientConfig.AuthenticationRegion = config.Region;

                _client = new AmazonS3Client(config.AccessKey, config.SecretKey, clientConfig);

                _isInitialized = true;
            }
        }

        #endregion

        #region Methods

        public string GetPublicUrl(string fileName)
        {
            var path = _appendBucketName ? $"{_bucketName}/" : string.Empty;
            return $"{_publicBaseUrl}/{path}{fileName}";
        }

        public async Task<bool> ExistsAsync(string fileName)
        {
            try
            {
                await _client.GetObjectMetadataAsync(_bucketName, fileName);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task UploadAsync(string fileName, byte[] binary, string mimeType, string cacheControl)
        {
            using var ms = new MemoryStream(binary);
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                InputStream = ms,
                AutoCloseStream = true
            };

            if (!string.IsNullOrWhiteSpace(mimeType))
                request.ContentType = mimeType;

            if (!string.IsNullOrWhiteSpace(cacheControl))
                request.Headers.CacheControl = cacheControl;

            await _client.PutObjectAsync(request);
        }

        public async Task DeleteByPrefixAsync(string prefix)
        {
            var listRequest = new ListObjectsV2Request { BucketName = _bucketName, Prefix = prefix };

            ListObjectsV2Response response;
            do
            {
                response = await _client.ListObjectsV2Async(listRequest);

                if (response.S3Objects.Count > 0)
                {
                    var deleteRequest = new DeleteObjectsRequest
                    {
                        BucketName = _bucketName,
                        Objects = response.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                    };
                    await _client.DeleteObjectsAsync(deleteRequest);
                }

                listRequest.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);
        }

        #endregion
    }
}
