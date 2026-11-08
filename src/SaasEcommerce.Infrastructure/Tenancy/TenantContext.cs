using SaasEcommerce.Core.Interfaces;

namespace SaasEcommerce.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId => CurrentTenant?.TenantId;
    public TenantContextDto? CurrentTenant { get; private set; }
    public bool IsSuperAdminContext { get; private set; }

    public void SetTenant(TenantContextDto tenant)
    {
        CurrentTenant = tenant;
        IsSuperAdminContext = false;
    }

    public void SetSuperAdminContext()
    {
        CurrentTenant = null;
        IsSuperAdminContext = true;
    }
}