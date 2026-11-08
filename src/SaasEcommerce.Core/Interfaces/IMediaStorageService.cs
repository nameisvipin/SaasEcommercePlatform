namespace SaasEcommerce.Core.Interfaces;

public record PresignedUploadResult(
    string UploadPutUrl,
    string PublicCdnUrl,
    string ObjectKey,
    int ExpiresInSeconds);

public interface IMediaStorageService
{
    PresignedUploadResult GeneratePresignedUploadUrl(
        Guid tenantId,
        string fileName,
        string contentType);
}