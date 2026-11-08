using SaasEcommerce.Core.Entities;

namespace SaasEcommerce.Core.Interfaces;

public record TenantContextDto(
    Guid TenantId,
    string StoreName,
    string Subdomain,
    string? CustomDomain,
    TenantStatus Status,
    bool IsHolidayMode,
    int HolidayExtraDeliveryDays);

public interface ITenantContext
{
    Guid? TenantId { get; }
    TenantContextDto? CurrentTenant { get; }
    bool IsSuperAdminContext { get; }
    void SetTenant(TenantContextDto tenant);
    void SetSuperAdminContext();
}