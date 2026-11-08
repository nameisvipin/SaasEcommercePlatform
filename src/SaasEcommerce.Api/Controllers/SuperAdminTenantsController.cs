using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;
using SaasEcommerce.Infrastructure.Security;

namespace SaasEcommerce.Api.Controllers;

public record ProvisionTenantRequest(
    string StoreName,
    string Subdomain,
    string? CustomDomain,
    string SellerEmail,
    string SellerFullName,
    string SellerPassword,
    string PrimaryColor,
    string SecondaryColor,
    string? RazorpayKeyId,
    string? RazorpayKeySecret);

[ApiController]
[Route("api/v1/superadmin/tenants")]
// [Authorize(Policy = "SuperAdminOnly")] // Uncomment after generating initial SuperAdmin token
public sealed class SuperAdminTenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantSecretProtector _protector;

    public SuperAdminTenantsController(AppDbContext db, ITenantSecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    [HttpPost]
    public async Task<IActionResult> ProvisionTenant([FromBody] ProvisionTenantRequest req, CancellationToken ct)
    {
        var normalizedSub = req.Subdomain.Trim().ToLowerInvariant();
        if (await _db.Tenants.AnyAsync(t => t.Subdomain == normalizedSub, ct))
        {
            return Conflict(new { error = "Subdomain is already taken." });
        }

        var tenantId = Guid.NewGuid();

        var tenant = new Tenant
        {
            Id = tenantId,
            StoreName = req.StoreName.Trim(),
            Subdomain = normalizedSub,
            CustomDomain = req.CustomDomain?.Trim().ToLowerInvariant(),
            Status = TenantStatus.Active,
            EncryptedRazorpayKeyId = !string.IsNullOrWhiteSpace(req.RazorpayKeyId)
                ? _protector.Encrypt(tenantId, req.RazorpayKeyId) : null,
            EncryptedRazorpayKeySecret = !string.IsNullOrWhiteSpace(req.RazorpayKeySecret)
                ? _protector.Encrypt(tenantId, req.RazorpayKeySecret) : null
        };

        var theme = new StorefrontTheme
        {
            TenantId = tenantId,
            PrimaryColor = req.PrimaryColor,
            SecondaryColor = req.SecondaryColor
        };

        var sellerUser = new AppUser
        {
            TenantId = tenantId,
            Email = req.SellerEmail.Trim().ToLowerInvariant(),
            FullName = req.SellerFullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.SellerPassword),
            Role = UserRole.ClientSeller
        };

        _db.Tenants.Add(tenant);
        _db.StorefrontThemes.Add(theme);
        _db.Users.Add(sellerUser);

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(ProvisionTenant), new
        {
            tenantId = tenant.Id,
            subdomain = tenant.Subdomain,
            customDomain = tenant.CustomDomain,
            sellerEmail = sellerUser.Email
        });
    }
}

[ApiController]
[Route("api/v1/storefront/bootstrap")]
public sealed class StorefrontBootstrapController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public StorefrontBootstrapController(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetStorefrontConfig(CancellationToken ct)
    {
        var tenant = _tenantContext.CurrentTenant!;

        // Global Query Filter automatically scopes this to CurrentTenantId!
        var theme = await _db.StorefrontThemes
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            tenantId = tenant.TenantId,
            storeName = tenant.StoreName,
            isHolidayMode = tenant.IsHolidayMode,
            holidayExtraDeliveryDays = tenant.HolidayExtraDeliveryDays,
            theme = new
            {
                primaryColor = theme?.PrimaryColor ?? "#1E3A8A",
                secondaryColor = theme?.SecondaryColor ?? "#0D9488",
                fontFamily = theme?.FontFamily ?? "Inter",
                borderRadius = theme?.BorderRadius ?? "8px",
                logoUrl = theme?.LogoUrl,
                layoutConfig = theme?.LayoutConfigJson ?? "{}"
            }
        });
    }
}