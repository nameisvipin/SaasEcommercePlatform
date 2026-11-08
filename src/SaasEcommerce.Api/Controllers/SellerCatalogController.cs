using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using SaasEcommerce.Core.Common;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Controllers;

public record CreateCategoryRequest(string Name, string? Description, Guid? ParentCategoryId, int DisplayOrder = 0);

public record CreateVariantDto(
    string Sku,
    string? Size,
    string? Color,
    string? Material,
    string? PackSize,
    decimal Mrp,
    decimal SellingPrice,
    decimal? SalePrice,
    int StockQty,
    int BufferStockQty,
    int LowStockAlertThreshold,
    decimal WeightGrams = 500m,
    bool IsDefault = false);

public record CreateMediaDto(
    MediaType MediaType,
    string Url,
    string ObjectKey,
    string? AltText,
    int DisplayOrder);

public record CreateProductRequest(
    Guid? CategoryId,
    string Title,
    string? BrandName,
    string? ShortDescription,
    string? RichDescriptionHtml,
    string HsnCode,
    decimal GstPercentage,
    List<string> BulletPoints,
    List<string> SeoKeywords,
    List<QuantityDiscountTier>? QuantityDiscounts,
    string? APlusContentJson,
    bool HasVariants,
    List<CreateVariantDto> Variants,
    List<CreateMediaDto> MediaGallery);

public record UpdateStockRequest(int StockQty, int BufferStockQty, int LowStockAlertThreshold);
public record ToggleHolidayModeRequest(bool IsHolidayMode, int ExtraDeliveryDays);
public record PresignedUploadRequest(string FileName, string ContentType);

public record UpdateStorefrontThemeRequest(
    string PrimaryColor,
    string SecondaryColor,
    string FontFamily,
    string BorderRadius,
    string? LogoUrl,
    string? FaviconUrl,
    string LayoutConfigJson);

[ApiController]
[Route("api/v1/seller")]
[Authorize(Policy = "SellerOnly")]
public sealed class SellerCatalogController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IMediaStorageService _mediaStorage;
    private readonly IDistributedCache _cache;

    public SellerCatalogController(
        AppDbContext db,
        ITenantContext tenantContext,
        IMediaStorageService mediaStorage,
        IDistributedCache cache)
    {
        _db = db;
        _tenantContext = tenantContext;
        _mediaStorage = mediaStorage;
        _cache = cache;
    }

    // 1. Create Category (FRD 3.1)
    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        var slug = GenerateSlug(req.Name);
        if (await _db.Categories.AnyAsync(c => c.Slug == slug, ct))
        {
            return Conflict(new { error = $"Category with slug '{slug}' already exists in your store." });
        }

        var category = new Category
        {
            Name = req.Name.Trim(),
            Slug = slug,
            Description = req.Description,
            ParentCategoryId = req.ParentCategoryId,
            DisplayOrder = req.DisplayOrder
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        await _cache.RemoveAsync($"tenant:{_tenantContext.TenantId}:categories", ct);

        return CreatedAtAction(nameof(CreateCategory), new { category.Id, category.Name, category.Slug });
    }

    // 2. Generate Cloudflare R2 Pre-Signed Upload URL (FRD 3.2)
    [HttpPost("media/presigned-url")]
    public IActionResult GetPresignedUploadUrl([FromBody] PresignedUploadRequest req)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var result = _mediaStorage.GeneratePresignedUploadUrl(tenantId, req.FileName, req.ContentType);
        return Ok(result);
    }

    // 3. Create Product with Variants, Media, Quantity Discounts & Auto Quality Score (FRD 3.1, 3.2, 3.3)
    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest req, CancellationToken ct)
    {
        if (req.Variants is null || req.Variants.Count == 0)
        {
            return BadRequest(new { error = "At least one SKU variant is required." });
        }

        foreach (var v in req.Variants)
        {
            if (v.SellingPrice > v.Mrp)
                return BadRequest(new { error = $"SKU '{v.Sku}': SellingPrice ({v.SellingPrice}) cannot exceed MRP ({v.Mrp})." });
        }

        var baseSlug = GenerateSlug(req.Title);
        var slug = await EnsureUniqueProductSlugAsync(baseSlug, ct);

        var product = new Product
        {
            CategoryId = req.CategoryId,
            Title = req.Title.Trim(),
            Slug = slug,
            BrandName = req.BrandName?.Trim(),
            ShortDescription = req.ShortDescription,
            RichDescriptionHtml = req.RichDescriptionHtml,
            HsnCode = req.HsnCode.Trim(),
            GstPercentage = req.GstPercentage,
            BulletPoints = req.BulletPoints ?? new(),
            SeoKeywords = req.SeoKeywords ?? new(),
            QuantityDiscounts = req.QuantityDiscounts ?? new(),
            APlusContentJson = string.IsNullOrWhiteSpace(req.APlusContentJson) ? "[]" : req.APlusContentJson,
            HasVariants = req.HasVariants,
            Status = ProductStatus.Active
        };

        foreach (var vDto in req.Variants)
        {
            product.Variants.Add(new ProductVariant
            {
                Sku = vDto.Sku.Trim().ToUpperInvariant(),
                Size = vDto.Size,
                Color = vDto.Color,
                Material = vDto.Material,
                PackSize = vDto.PackSize,
                Mrp = vDto.Mrp,
                SellingPrice = vDto.SellingPrice,
                SalePrice = vDto.SalePrice,
                StockQty = vDto.StockQty,
                BufferStockQty = vDto.BufferStockQty,
                LowStockAlertThreshold = vDto.LowStockAlertThreshold,
                WeightGrams = vDto.WeightGrams,
                IsDefault = vDto.IsDefault
            });
        }

        if (req.MediaGallery is not null)
        {
            foreach (var mDto in req.MediaGallery)
            {
                product.MediaGallery.Add(new ProductMedia
                {
                    MediaType = mDto.MediaType,
                    Url = mDto.Url,
                    ObjectKey = mDto.ObjectKey,
                    AltText = mDto.AltText ?? product.Title,
                    DisplayOrder = mDto.DisplayOrder
                });
            }
        }

        product.QualityScore = CatalogQualityCalculator.CalculateScore(product);

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetSellerProducts), new
        {
            productId = product.Id,
            slug = product.Slug,
            qualityScore = product.QualityScore,
            variantCount = product.Variants.Count
        });
    }

    // 4. List All Products & Low-Stock Status in Seller Portal
    [HttpGet("products")]
    public async Task<IActionResult> GetSellerProducts(CancellationToken ct)
    {
        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.MediaGallery.OrderBy(m => m.DisplayOrder))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                CategoryName = p.Category != null ? p.Category.Name : "Uncategorized",
                p.HsnCode,
                p.GstPercentage,
                p.Status,
                p.QualityScore,
                ThumbnailUrl = p.MediaGallery.OrderBy(m => m.DisplayOrder).Select(m => m.Url).FirstOrDefault(),
                Variants = p.Variants.Select(v => new
                {
                    v.Id,
                    v.Sku,
                    v.Size,
                    v.Color,
                    v.Mrp,
                    v.SellingPrice,
                    v.StockQty,
                    v.BufferStockQty,
                    SellableStock = Math.Max(0, v.StockQty - v.BufferStockQty),
                    IsLowStock = Math.Max(0, v.StockQty - v.BufferStockQty) <= v.LowStockAlertThreshold
                })
            })
            .ToListAsync(ct);

        return Ok(products);
    }

    // 5. Real-Time SKU Stock & Buffer Update + Instant PDP Cache Purge (FRD 3.4 & Arch 4.2)
    [HttpPatch("inventory/{variantId:guid}")]
    public async Task<IActionResult> UpdateVariantStock(
        Guid variantId,
        [FromBody] UpdateStockRequest req,
        CancellationToken ct)
    {
        var variant = await _db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == variantId, ct);

        if (variant is null) return NotFound(new { error = "Variant SKU not found in your store." });

        variant.StockQty = Math.Max(0, req.StockQty);
        variant.BufferStockQty = Math.Max(0, req.BufferStockQty);
        variant.LowStockAlertThreshold = Math.Max(0, req.LowStockAlertThreshold);

        await _db.SaveChangesAsync(ct);

        // Immediately purge cached Product Detail Page in Redis (Arch 4.2)
        if (variant.Product is not null)
        {
            await _cache.RemoveAsync($"tenant:{_tenantContext.TenantId}:pdp:{variant.Product.Slug}", ct);
        }

        return Ok(new
        {
            variant.Id,
            variant.Sku,
            variant.StockQty,
            variant.BufferStockQty,
            variant.SellableStock,
            variant.IsLowStock
        });
    }

    // 6. Toggle Store Holiday Mode & Purge Edge Cache (FRD 3.4 & Arch 4.2)
    [HttpPatch("store/holiday-mode")]
    public async Task<IActionResult> ToggleHolidayMode(
        [FromBody] ToggleHolidayModeRequest req,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        tenant.IsHolidayMode = req.IsHolidayMode;
        tenant.HolidayExtraDeliveryDays = req.IsHolidayMode ? Math.Max(1, req.ExtraDeliveryDays) : 0;

        await _db.SaveChangesAsync(ct);

        await _cache.RemoveAsync($"tenant:slug:{tenant.Subdomain}", ct);
        if (!string.IsNullOrEmpty(tenant.CustomDomain))
        {
            await _cache.RemoveAsync($"tenant:host:{tenant.CustomDomain}", ct);
        }

        return Ok(new
        {
            tenant.Id,
            tenant.StoreName,
            tenant.IsHolidayMode,
            tenant.HolidayExtraDeliveryDays,
            message = tenant.IsHolidayMode
                ? $"Holiday Mode enabled. Buyers will see +{tenant.HolidayExtraDeliveryDays} extra days on delivery estimates."
                : "Holiday Mode disabled. Standard delivery timelines restored."
        });
    }

    // 7. Update Custom Storefront Theme & Modular Homepage Sections (Tier 1 & Tier 2 Custom UI)
    [HttpPut("store/theme")]
    public async Task<IActionResult> UpdateStorefrontTheme(
        [FromBody] UpdateStorefrontThemeRequest req,
        CancellationToken ct)
    {
        var theme = await _db.StorefrontThemes.FirstOrDefaultAsync(ct);
        if (theme is null)
        {
            theme = new StorefrontTheme { TenantId = _tenantContext.TenantId!.Value };
            _db.StorefrontThemes.Add(theme);
        }

        theme.PrimaryColor = req.PrimaryColor;
        theme.SecondaryColor = req.SecondaryColor;
        theme.FontFamily = req.FontFamily;
        theme.BorderRadius = req.BorderRadius;
        theme.LogoUrl = req.LogoUrl;
        theme.FaviconUrl = req.FaviconUrl;
        theme.LayoutConfigJson = string.IsNullOrWhiteSpace(req.LayoutConfigJson) ? "{}" : req.LayoutConfigJson;

        await _db.SaveChangesAsync(ct);

        await _cache.RemoveAsync($"tenant:{_tenantContext.TenantId}:bootstrap", ct);

        return Ok(new { message = "Storefront theme and modular layout updated.", theme });
    }

private static string GenerateSlug(string phrase)
    {
        string str = phrase.ToLowerInvariant().Trim();
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", "-");
        str = Regex.Replace(str, @"-+", "-").Trim('-'); // Collapses multiple dashes into one
        return str;
    }

    private async Task<string> EnsureUniqueProductSlugAsync(string baseSlug, CancellationToken ct)
    {
        var slug = baseSlug;
        int counter = 1;
        while (await _db.Products.AnyAsync(p => p.Slug == slug, ct))
        {
            slug = $"{baseSlug}-{counter++}";
        }
        return slug;
    }
}