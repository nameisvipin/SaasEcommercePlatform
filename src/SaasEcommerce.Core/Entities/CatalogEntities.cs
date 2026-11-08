using SaasEcommerce.Core.Common;

namespace SaasEcommerce.Core.Entities;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
    SuppressedByAdmin = 99 // FRD 3.1: Super-Admin can suppress policy-violating listings
}

public class Category : TenantScopedEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public int DisplayOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
}

public class Product : TenantScopedEntity
{
    public Guid? CategoryId { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? BrandName { get; set; }
    public string? ShortDescription { get; set; }
    public string? RichDescriptionHtml { get; set; }

    // Tax & Compliance (FRD 3.1)
    public required string HsnCode { get; set; } // 4 to 8 digit GST HSN code
    public decimal GstPercentage { get; set; } = 18.0m; // e.g., 5%, 12%, 18%, 28%

    // Structured JSONB fields for fast single-query reads
    public List<string> BulletPoints { get; set; } = new();
    public List<string> SeoKeywords { get; set; } = new();
    
    /// <summary>
    /// JSONB storing Enhanced Brand Content / Basic A+ modules below the fold (FRD 3.2)
    /// </summary>
    public string APlusContentJson { get; set; } = "[]";

    public bool HasVariants { get; set; } = false;
    public ProductStatus Status { get; set; } = ProductStatus.Active;

    /// <summary>
    /// Auto-calculated 0-100 completeness score (FRD 3.1)
    /// </summary>
    public int QualityScore { get; set; } = 0;

    // Platform Trust Badges assigned by Super-Admin (FRD 2.4 & 3.5)
    public bool IsAssuredQualityBadge { get; set; } = false;
    public int ReplacementDays { get; set; } = 7;

    public Category? Category { get; set; }
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductMedia> MediaGallery { get; set; } = new List<ProductMedia>();
}

public class ProductVariant : TenantScopedEntity
{
    public Guid ProductId { get; set; }
    public required string Sku { get; set; }

    // Variant Attributes (FRD 3.1)
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Material { get; set; }
    public string? PackSize { get; set; }

    // Pricing & Scheduled Sales (FRD 3.3)
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public DateTimeOffset? SaleStartAt { get; set; }
    public DateTimeOffset? SaleEndAt { get; set; }

    // Inventory & Low-Stock Alerts (FRD 3.4)
    public int StockQty { get; set; } = 0;
    public int BufferStockQty { get; set; } = 0;
    public int LowStockAlertThreshold { get; set; } = 5;

    // Shipping Dimensions for Shiprocket Courier Calculation (FRD 3.1)
    public decimal WeightGrams { get; set; } = 500m;
    public decimal LengthCm { get; set; } = 15m;
    public decimal WidthCm { get; set; } = 15m;
    public decimal HeightCm { get; set; } = 5m;

    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Available sellable stock = StockQty minus safety BufferStockQty
    /// </summary>
    public int SellableStock => Math.Max(0, StockQty - BufferStockQty);

    public bool IsLowStock => SellableStock <= LowStockAlertThreshold;

    public Product? Product { get; set; }
}

public enum MediaType
{
    ImageWebP = 1,
    VideoMp4 = 2,
    View360 = 3
}

public class ProductMedia : TenantScopedEntity
{
    public Guid ProductId { get; set; }
    public MediaType MediaType { get; set; } = MediaType.ImageWebP;
    public required string Url { get; set; }
    public required string ObjectKey { get; set; } // R2 Bucket Key: tenants/{tenantId}/products/{file}.webp
    public string? ThumbnailUrl { get; set; }
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; } = 0;

    public Product? Product { get; set; }
}