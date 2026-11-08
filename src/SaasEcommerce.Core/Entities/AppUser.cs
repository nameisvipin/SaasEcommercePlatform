using SaasEcommerce.Core.Common;

namespace SaasEcommerce.Core.Entities;

public enum UserRole
{
    Buyer = 1,
    ClientSeller = 2,
    PlatformSuperAdmin = 99
}

public class AppUser : BaseEntity
{
    /// <summary>
    /// Null ONLY for PlatformSuperAdmin. Mandatory for ClientSeller and Buyer.
    /// </summary>
    public Guid? TenantId { get; set; }
    
    public required string Email { get; set; }
    public string? PhoneNumber { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public UserRole Role { get; set; } = UserRole.Buyer;
    
    public bool IsTwoFactorEnabled { get; set; } = false;
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Incremented whenever the user clicks "Log out of all active devices" (FRD 2.1)
    /// Validated against JWT claim to immediately revoke old tokens.
    /// </summary>
    public int SecurityStampVersion { get; set; } = 1;
}