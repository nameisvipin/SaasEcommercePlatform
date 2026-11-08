using Microsoft.AspNetCore.DataProtection;

namespace SaasEcommerce.Infrastructure.Security;

public interface ITenantSecretProtector
{
    string Encrypt(Guid tenantId, string plainTextSecret);
    string Decrypt(Guid tenantId, string cipherTextSecret);
}

public sealed class TenantSecretProtector : ITenantSecretProtector
{
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public TenantSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    private IDataProtector GetProtector(Guid tenantId)
    {
        // Binds cryptographic key derivation specifically to that Tenant's UUID
        // Even if ciphertexts are copied between rows in SQL, decryption will fail!
        return _dataProtectionProvider.CreateProtector($"SaasEcommerce.BYOK.v1.Tenant:{tenantId}");
    }

    public string Encrypt(Guid tenantId, string plainTextSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainTextSecret);
        return GetProtector(tenantId).Protect(plainTextSecret);
    }

    public string Decrypt(Guid tenantId, string cipherTextSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cipherTextSecret);
        return GetProtector(tenantId).Unprotect(cipherTextSecret);
    }
}