using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SaasEcommerce.Core.Common;
using SaasEcommerce.Core.Interfaces;

namespace SaasEcommerce.Infrastructure.Persistence.Interceptors;

public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantSaveChangesInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTimeOffset.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        // Skip strict tenant stamping only if Platform SuperAdmin is explicitly operating
        if (_tenantContext.IsSuperAdminContext)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var currentTenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Security Violation: Attempting to save tenant-scoped entity without an active TenantContext.");

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Forcefully overwrite TenantId with the verified context TenantId
                    entry.Entity.TenantId = currentTenantId;
                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    // Prevent cross-tenant tampering
                    entry.Property(x => x.TenantId).IsModified = false;
                    if (entry.Entity.TenantId != currentTenantId)
                    {
                        throw new UnauthorizedAccessException("Security Violation: Cross-tenant data modification detected.");
                    }
                    break;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}