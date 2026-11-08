using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Middleware;

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private readonly string _masterPlatformDomain;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _masterPlatformDomain = config["Platform:MasterDomain"] ?? "localhost";
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        AppDbContext dbContext,
        IDistributedCache cache)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 1. Allow Super-Admin & Healthcheck routes to execute in Master Context
        if (path.StartsWith("/api/v1/superadmin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            tenantContext.SetSuperAdminContext();
            await _next(context);
            return;
        }

        // 2. Extract Host (Supports Cloudflare Custom Hostname header X-Forwarded-Host or Explicit Header for Headless Tier 3)
        string? host = context.Request.Headers["X-Custom-Tenant-Domain"].FirstOrDefault()
                       ?? context.Request.Headers["X-Forwarded-Host"].FirstOrDefault()
                       ?? context.Request.Host.Host;

        host = host.Trim().ToLowerInvariant();

        // Optional explicit Tenant Slug header for local dev or Tier 3 Headless frontends
        string? explicitSubdomain = context.Request.Headers["X-Tenant-Slug"].FirstOrDefault()?.Trim().ToLowerInvariant();

        string lookupKey = !string.IsNullOrEmpty(explicitSubdomain)
            ? $"tenant:slug:{explicitSubdomain}"
            : $"tenant:host:{host}";

        // 3. Sub-millisecond Redis Cache Lookup
        TenantContextDto? resolvedTenant = null;
        var cachedJson = await cache.GetStringAsync(lookupKey, context.RequestAborted);

        if (!string.IsNullOrEmpty(cachedJson))
        {
            resolvedTenant = JsonSerializer.Deserialize<TenantContextDto>(cachedJson);
        }
        else
        {
            // 4. Cache Miss -> Query PostgreSQL Tenants Table (IgnoreQueryFilters since Tenants is master table)
            Tenant? tenantEntity = null;

            if (!string.IsNullOrEmpty(explicitSubdomain))
            {
                tenantEntity = await dbContext.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Subdomain == explicitSubdomain, context.RequestAborted);
            }
            else if (host.EndsWith($".{_masterPlatformDomain}", StringComparison.OrdinalIgnoreCase))
            {
                var sub = host.Replace($".{_masterPlatformDomain}", "");
                tenantEntity = await dbContext.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Subdomain == sub, context.RequestAborted);
            }
            else
            {
                // Custom Domain lookup (e.g., clientbrand.com)
                tenantEntity = await dbContext.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.CustomDomain == host, context.RequestAborted);
            }

            if (tenantEntity is not null)
            {
                resolvedTenant = new TenantContextDto(
                    tenantEntity.Id,
                    tenantEntity.StoreName,
                    tenantEntity.Subdomain,
                    tenantEntity.CustomDomain,
                    tenantEntity.Status,
                    tenantEntity.IsHolidayMode,
                    tenantEntity.HolidayExtraDeliveryDays);

                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                };

                await cache.SetStringAsync(
                    lookupKey,
                    JsonSerializer.Serialize(resolvedTenant),
                    cacheOptions,
                    context.RequestAborted);
            }
        }

        if (resolvedTenant is null)
        {
            _logger.LogWarning("Unrecognized tenant host or slug: {Host} / {Slug}", host, explicitSubdomain);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Storefront not found or domain not configured." });
            return;
        }

        if (resolvedTenant.Status == TenantStatus.Suspended)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "This store is currently unavailable." });
            return;
        }

        // 5. Bind Tenant to Request Scope
        tenantContext.SetTenant(resolvedTenant);

        await _next(context);
    }
}