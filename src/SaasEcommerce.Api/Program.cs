using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SaasEcommerce.Api.Middleware;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;
using SaasEcommerce.Infrastructure.Persistence.Interceptors;
using SaasEcommerce.Infrastructure.Security;
using SaasEcommerce.Infrastructure.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// 1. Data Protection for BYOK Secrets (Persist keys to mounted volume in Docker)
builder.Services.AddDataProtection()
    .SetApplicationName("SaasEcommercePlatform")
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["Security:KeyRingPath"] ?? "./keys"));

builder.Services.AddSingleton<ITenantSecretProtector, TenantSecretProtector>();

// 2. Scoped Tenant Context & EF Interceptors
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantSaveChangesInterceptor>();
builder.Services.AddScoped<RlsConnectionInterceptor>();

// 3. Read/Write Decoupled Database Registration (Connects via PgBouncer on Port 6432)
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("MasterConnection");
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(3);
    })
    .AddInterceptors(
        sp.GetRequiredService<RlsConnectionInterceptor>(),
        sp.GetRequiredService<TenantSaveChangesInterceptor>());
});

// 4. Redis Distributed Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
    options.InstanceName = "SaasPlatform:";
});

// 5. Multi-Tenant Aware JWT Authentication
var jwtKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("JWT SecretKey is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
                var principal = context.Principal;

                if (principal is null)
                {
                    context.Fail("Invalid principal.");
                    return;
                }

                var roleClaim = principal.FindFirstValue(ClaimTypes.Role);
                var tokenTenantClaim = principal.FindFirstValue("tenant_id");

                // SuperAdmin can access SuperAdmin routes
                if (roleClaim == nameof(UserRole.PlatformSuperAdmin))
                {
                    return;
                }

                // For Seller and Buyer tokens, strictly enforce TenantId match against the domain!
                if (tenantContext.TenantId is null ||
                    !Guid.TryParse(tokenTenantClaim, out var tokenTenantId) ||
                    tokenTenantId != tenantContext.TenantId.Value)
                {
                    context.Fail("Security Violation: Token tenant_id does not match current storefront domain.");
                }
            }
        };
    });

// 6. Role-Based Authorization Policies (FRD Section 5 Matrix)
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SuperAdminOnly", p => p.RequireRole(nameof(UserRole.PlatformSuperAdmin)))
    .AddPolicy("SellerOnly", p => p.RequireRole(nameof(UserRole.ClientSeller), nameof(UserRole.PlatformSuperAdmin)))
    .AddPolicy("BuyerOrSeller", p => p.RequireRole(nameof(UserRole.Buyer), nameof(UserRole.ClientSeller)));

builder.Services.AddControllers();

var app = builder.Build();

// Forwarded Headers for Cloudflare Proxy / Coolify
app.UseForwardedHeaders();

// Register Tenant Resolution Middleware BEFORE Authentication & Authorization
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();