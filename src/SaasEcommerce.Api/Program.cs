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
using SaasEcommerce.Infrastructure.Storage;
using SaasEcommerce.Infrastructure.Tenancy;
using Npgsql;
using SaasEcommerce.Infrastructure.Invoices;
using SaasEcommerce.Infrastructure.Logistics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

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
builder.Services.AddSingleton<ITokenGenerator, TokenGenerator>();
builder.Services.AddSingleton<IMediaStorageService, CloudflareR2StorageService>();
builder.Services.AddSingleton<IInvoicePdfService, QuestPdfInvoiceService>();
builder.Services.AddSingleton<ILogisticsService, ShiprocketLogisticsService>();

// 3. Npgsql DataSource with Dynamic JSON Enabled (Npgsql 8.0+)
var connectionString = builder.Configuration.GetConnectionString("MasterConnection");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(dataSource, npgsql =>
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
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var principal = context.Principal;

                if (principal is null)
                {
                    context.Fail("Invalid principal.");
                    return;
                }

                var roleClaim = principal.FindFirstValue(ClaimTypes.Role);
                var tokenTenantClaim = principal.FindFirstValue("tenant_id");
                var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var stampClaim = principal.FindFirstValue("security_stamp");

                if (roleClaim != nameof(UserRole.PlatformSuperAdmin))
                {
                    if (tenantContext.TenantId is null ||
                        !Guid.TryParse(tokenTenantClaim, out var tokenTenantId) ||
                        tokenTenantId != tenantContext.TenantId.Value)
                    {
                        context.Fail("Security Violation: Token tenant_id does not match current storefront domain.");
                        return;
                    }
                }

                // Verify user is active and SecurityStampVersion matches (FRD 2.1 Force Logout All Devices)
                if (Guid.TryParse(userIdClaim, out var userId))
                {
                    var userStamp = await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId && u.IsActive)
                        .Select(u => (int?)u.SecurityStampVersion)
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                    if (userStamp is null || userStamp.Value.ToString() != stampClaim)
                    {
                        context.Fail("Session has been revoked or user logged out of all devices.");
                    }
                }
            }
        };
    });

// 6. Role-Based Authorization Policies (FRD Section 5 Matrix)
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SuperAdminOnly", p => p.RequireRole(nameof(UserRole.PlatformSuperAdmin)))
    .AddPolicy("SellerOnly", p => p.RequireRole(nameof(UserRole.ClientSeller), nameof(UserRole.PlatformSuperAdmin)))
    .AddPolicy("BuyerOrSeller", p => p.RequireRole(nameof(UserRole.Buyer), nameof(UserRole.ClientSeller), nameof(UserRole.PlatformSuperAdmin)));

builder.Services.AddControllers();

// 1. Dynamic Multi-Tenant CORS Policy for Angular 18
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowStorefrontAndAdminOrigins", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            // Allow local Angular dev servers
            if (origin.StartsWith("http://localhost:4200") || origin.StartsWith("http://127.0.0.1:4200"))
                return true;

            // Allow your production platform domains and merchant subdomains / custom domains
            var uri = new Uri(origin);
            return uri.Host.EndsWith("yourplatform.com") || 
                   uri.Host.EndsWith("urbanthreads.in");
        })
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()
        // Expose headers so Angular can read file names and pagination headers
        .WithExposedHeaders("Content-Disposition", "X-Total-Count", "X-Pagination");
    });
});

// 2. Built-in .NET 8 Fixed-Window Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict limiter for authentication endpoints (prevents brute-force)
    options.AddPolicy("AuthRateLimit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Standard limiter for public storefront searches & browsing
    options.AddPolicy("StorefrontRateLimit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

// using (var scope = app.Services.CreateScope())
// {
//     var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
//     var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

//     // 1. Tell TenantContext we are SuperAdmin so TenantSaveChangesInterceptor allows saving with TenantId = null
//     tenantContext.SetSuperAdminContext();

//     // 2. Set PostgreSQL RLS session variable
//     await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.current_tenant', 'SUPERADMIN', true);");

//     var superAdmin = await db.Users
//         .IgnoreQueryFilters()
//         .FirstOrDefaultAsync(u => u.Email == "admin@yourplatform.com");

//     if (superAdmin is null)
//     {
//         db.Users.Add(new AppUser
//         {
//             Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
//             TenantId = null,
//             Email = "admin@yourplatform.com",
//             PhoneNumber = "9999999999",
//             FullName = "Platform Super Admin",
//             Role = UserRole.PlatformSuperAdmin,
//             PasswordHash = BCrypt.Net.BCrypt.HashPassword("SuperAdminPassword#2026"),
//             IsActive = true,
//             SecurityStampVersion = 1
//         });
//     }
//     else
//     {
//         superAdmin.Role = UserRole.PlatformSuperAdmin;
//         superAdmin.TenantId = null;
//         superAdmin.IsActive = true;
//         superAdmin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("SuperAdminPassword#2026");
//     }

//     await db.SaveChangesAsync();
// }

// Forwarded Headers for Cloudflare Proxy / Coolify
app.UseForwardedHeaders();

// Register Tenant Resolution Middleware BEFORE Authentication & Authorization
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseCors("AllowStorefrontAndAdminOrigins");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();