using Newtonsoft.Json;

namespace Nop.Core.Configuration
{
    /// <summary>
    /// Represents S3-compatible storage configuration parameters (real AWS S3,
    /// or any S3-compatible endpoint such as a self-hosted Garage/MinIO cluster).
    /// Sibling of <see cref="AzureBlobConfig"/> - see Nop.Services.Media.
    /// S3BlobStorageProvider/CloudPictureService for how this is used.
    /// </summary>
    public partial class S3Config : IConfig
    {
        /// <summary>
        /// Gets or sets the S3-compatible endpoint to talk to (e.g.
        /// "http://garage.kube-system.svc.cluster.local:3900" in-cluster, or
        /// left empty to use AWS S3's own regional endpoints).
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the public, browser-reachable base URL for reads, when
        /// different from <see cref="ServiceUrl"/> (e.g. Garage's in-cluster
        /// ServiceUrl for uploads vs its public https endpoint for thumbnail
        /// URLs actually served to customers). Falls back to ServiceUrl when empty.
        /// </summary>
        public string PublicBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the bucket name.
        /// </summary>
        public string BucketName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the S3 access key.
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the S3 secret key.
        /// </summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the region used for SigV4 request signing (e.g. "garage"
        /// for a Garage cluster - it rejects the AWS SDK's "us-east-1" default).
        /// </summary>
        public string Region { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether to address objects as "endpoint/bucket/key"
        /// (path-style) instead of "bucket.endpoint/key" (virtual-hosted-style,
        /// AWS's own default). Most self-hosted S3-compatible stores, including
        /// Garage, need path-style since virtual-hosted-style requires
        /// per-bucket DNS/TLS the store doesn't set up.
        /// </summary>
        public bool ForcePathStyle { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the bucket name is appended when constructing
        /// the public URL - only relevant when <see cref="PublicBaseUrl"/>
        /// doesn't already resolve straight to the bucket (e.g. a plain S3
        /// endpoint with path-style addressing needs it; a bucket-specific CDN
        /// domain would not).
        /// </summary>
        public bool AppendBucketName { get; set; } = true;

        /// <summary>
        /// Gets a value indicating whether we should use S3-compatible storage.
        /// </summary>
        [JsonIgnore]
        public bool Enabled => !string.IsNullOrEmpty(BucketName) && !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey);
    }
}
