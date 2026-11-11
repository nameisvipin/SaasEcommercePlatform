using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly ITenantContext _tenantContext;

    public AuthController(
        AppDbContext db,
        ITokenGenerator tokenGenerator,
        ITenantContext tenantContext)
    {
        _db = db;
        _tokenGenerator = tokenGenerator;
        _tenantContext = tenantContext;
    }

    [EnableRateLimiting("AuthRateLimit")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();

        // If not in SuperAdmin context, Global Query Filter limits search to this tenant only
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        bool isPasswordValid = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Generate Token
        var token = _tokenGenerator.GenerateJwtToken(
            user.Id,
            user.Email,
            user.Role.ToString(),
            user.TenantId,
            user.SecurityStampVersion);

        return Ok(new AuthResponse(
            AccessToken: token,
            ExpiresInSeconds: 7 * 24 * 3600,
            TokenType: "Bearer",
            Email: user.Email,
            FullName: user.FullName,
            Role: user.Role.ToString(),
            TenantId: user.TenantId));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null) return NotFound();

        return Ok(new
        {
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            user.TenantId,
            user.CreatedAt
        });
    }

    public record BuyerRegisterRequest(string Email, string PhoneNumber, string FullName, string Password);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    // Buyer Registration on Client Storefront (FRD 2.1)
    [HttpPost("register")]
    public async Task<IActionResult> RegisterBuyer([FromBody] BuyerRegisterRequest req, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            return BadRequest(new { error = "Tenant context is required for buyer registration." });

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == normalizedEmail, ct))
        {
            return Conflict(new { error = "An account with this email already exists on this store." });
        }

        var buyer = new AppUser
        {
            TenantId = _tenantContext.TenantId.Value,
            Email = normalizedEmail,
            PhoneNumber = req.PhoneNumber.Trim(),
            FullName = req.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = UserRole.Buyer
        };

        _db.Users.Add(buyer);
        await _db.SaveChangesAsync(ct);

        var token = _tokenGenerator.GenerateJwtToken(
            buyer.Id, buyer.Email, buyer.Role.ToString(), buyer.TenantId, buyer.SecurityStampVersion);

        return CreatedAtAction(nameof(GetProfile), new AuthResponse(
            token, 7 * 24 * 3600, "Bearer", buyer.Email, buyer.FullName, buyer.Role.ToString(), buyer.TenantId));
    }

    public record SuperAdminLoginRequest(string Email, string Password);

    // Dedicated Platform Super-Admin Login (Tenant-Agnostic, No Tenant Header Required)
    [HttpPost("superadmin/login")]
    [AllowAnonymous]
    public async Task<IActionResult> SuperAdminLogin([FromBody] SuperAdminLoginRequest req, CancellationToken ct)
    {
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();

        // Super-Admins have TenantId = null; IgnoreQueryFilters bypasses the tenant filter
        var superAdmin = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && 
                                      u.Role == UserRole.PlatformSuperAdmin && 
                                      u.TenantId == null, ct);

        if (superAdmin is null || !superAdmin.IsActive)
        {
            return Unauthorized(new { error = "Invalid platform super-admin credentials." });
        }

        bool isPasswordValid = BCrypt.Net.BCrypt.Verify(req.Password, superAdmin.PasswordHash);
        if (!isPasswordValid)
        {
            return Unauthorized(new { error = "Invalid platform super-admin credentials." });
        }

        var token = _tokenGenerator.GenerateJwtToken(
            superAdmin.Id,
            superAdmin.Email,
            superAdmin.Role.ToString(),
            tenantId: null,
            superAdmin.SecurityStampVersion);

        return Ok(new AuthResponse(
            AccessToken: token,
            ExpiresInSeconds: 7 * 24 * 3600,
            TokenType: "Bearer",
            Email: superAdmin.Email,
            FullName: superAdmin.FullName,
            Role: superAdmin.Role.ToString(),
            TenantId: null
        ));
    }

    // Force-Logout of All Active Devices (FRD 2.1)
    [HttpPost("logout-all-devices")]
    [Authorize]
    public async Task<IActionResult> LogoutAllDevices(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        user.SecurityStampVersion += 1;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "All active sessions across all devices have been revoked.", newSecurityStamp = user.SecurityStampVersion });
    }
}