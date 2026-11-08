using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SaasEcommerce.Core.Interfaces;

namespace SaasEcommerce.Infrastructure.Persistence.Interceptors;

public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    public RlsConnectionInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        var tenantValue = _tenantContext.IsSuperAdminContext
            ? "SUPERADMIN"
            : (_tenantContext.TenantId?.ToString() ?? string.Empty);

        // Sets session variable for PostgreSQL RLS policy evaluation
        cmd.CommandText = $"SELECT set_config('app.current_tenant', '{tenantValue}', false);";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}