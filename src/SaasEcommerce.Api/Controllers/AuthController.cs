using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}