namespace SaasEcommerce.Core.Interfaces;

public record LoginRequest(string Email, string Password);

public record AuthResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string TokenType,
    string Email,
    string FullName,
    string Role,
    Guid? TenantId);

public interface ITokenGenerator
{
    string GenerateJwtToken(Guid userId, string email, string role, Guid? tenantId, int securityStampVersion);
}