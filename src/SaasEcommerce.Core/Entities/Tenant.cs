using SaasEcommerce.Core.Common;

namespace SaasEcommerce.Core.Entities;

public enum TenantStatus
{
    PendingSetup = 0,
    Active = 1,
    HolidayMode = 2,
    Suspended = 3
}

public class Tenant : BaseEntity
{
    public required string StoreName { get; set; }
    
    /// <summary>
    /// E.g., "urbanthreads" -> urbanthreads.yourplatform.com
    /// </summary>
    public required string Subdomain { get; set; }
    
    /// <summary>
    /// E.g., "shop.urbanthreads.in" or "urbanthreads.in" (Cloudflare Custom Hostname)
    /// </summary>
    public string? CustomDomain { get; set; }
    
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    
    // Holiday Mode Settings (FRD 3.4)
    public bool IsHolidayMode { get; set; } = false;
    public int HolidayExtraDeliveryDays { get; set; } = 0;

    // GST & Legal Invoicing Info (FRD 2.6)
    public string? Gstin { get; set; }
    public string? LegalBusinessName { get; set; }
    public string? RegisteredStateCode { get; set; } // E.g., "07" for Delhi

    // Encrypted BYOK Credentials (Encrypted via IDataProtectionProvider)
    public string? EncryptedRazorpayKeyId { get; set; }
    public string? EncryptedRazorpayKeySecret { get; set; }
    public string? EncryptedRazorpayWebhookSecret { get; set; }
    public string? EncryptedShiprocketApiToken { get; set; }

    public StorefrontTheme? Theme { get; set; }
}

public class StorefrontTheme : TenantScopedEntity
{
    public string PrimaryColor { get; set; } = "#1E3A8A";
    public string SecondaryColor { get; set; } = "#0D9488";
    public string FontFamily { get; set; } = "Inter";
    public string BorderRadius { get; set; } = "8px";
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    
    /// <summary>
    /// JSONB column storing Tier 1 & Tier 2 modular homepage sections & layout flags
    /// </summary>
    public string LayoutConfigJson { get; set; } = "{}";
}