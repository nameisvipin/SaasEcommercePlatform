using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using SaasEcommerce.Core.Interfaces;

namespace SaasEcommerce.Infrastructure.Storage;

public sealed class CloudflareR2StorageService : IMediaStorageService
{
    private readonly IConfiguration _config;
    private readonly IAmazonS3? _s3Client;
    private readonly string _bucketName;
    private readonly string _publicCdnBaseUrl;
    private readonly bool _isConfigured;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/webp",
        "image/jpeg",
        "image/png",
        "video/mp4",
        "video/webm"
    };

    public CloudflareR2StorageService(IConfiguration config)
    {
        _config = config;
        var accountId = _config["CloudflareR2:AccountId"];
        var accessKey = _config["CloudflareR2:AccessKeyId"];
        var secretKey = _config["CloudflareR2:SecretAccessKey"];
        _bucketName = _config["CloudflareR2:BucketName"] ?? "saas-media-bucket";
        _publicCdnBaseUrl = (_config["CloudflareR2:PublicCdnUrl"] ?? "https://cdn.yourplatform.com").TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(accountId) &&
            !string.IsNullOrWhiteSpace(accessKey) &&
            !string.IsNullOrWhiteSpace(secretKey) &&
            !accountId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                ForcePathStyle = true
            };
            _s3Client = new AmazonS3Client(credentials, s3Config);
            _isConfigured = true;
        }
    }

    public PresignedUploadResult GeneratePresignedUploadUrl(
        Guid tenantId,
        string fileName,
        string contentType)
    {
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new ArgumentException($"Unsupported media content type: {contentType}. Allowed: WebP, JPEG, PNG, MP4, WebM.");
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".webp";

        // Scope R2 object key by tenantId
        var objectKey = $"tenants/{tenantId}/products/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{ext}";
        var publicUrl = $"{_publicCdnBaseUrl}/{objectKey}";

        // Local Development Fallback if R2 credentials are not configured
        if (!_isConfigured || _s3Client is null)
        {
            var mockUploadUrl = $"https://mock-r2-upload.local/{_bucketName}/{objectKey}?signature=dev_mode_sig";
            return new PresignedUploadResult(mockUploadUrl, publicUrl, objectKey, 900);
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        var uploadUrl = _s3Client.GetPreSignedURL(request);
        return new PresignedUploadResult(uploadUrl, publicUrl, objectKey, 900);
    }
}