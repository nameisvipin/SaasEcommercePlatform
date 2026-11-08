using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Controllers;

public sealed record CatalogFilterQuery(
    string? Q = null,
    string? CategorySlug = null,
    string? Brand = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    decimal? MinDiscount = null,
    decimal? MinRating = null,
    bool? InStockOnly = null,
    bool? AssuredOnly = null,
    string SortBy = "newest",
    int Page = 1,
    int PageSize = 20);

[ApiController]
[Route("api/v1/storefront")]
public sealed class StorefrontCatalogController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedCache _cache;

    public StorefrontCatalogController(
        AppDbContext db,
        ITenantContext tenantContext,
        IDistributedCache cache)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    // 1. Hierarchical Category Navigation Tree (Cached in Redis) (FRD 2.4)
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var cacheKey = $"tenant:{tenantId}:categories";

        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cached))
        {
            return Content(cached, "application/json");
        }

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.Description,
                c.ParentCategoryId,
                c.DisplayOrder
            })
            .ToListAsync(ct);

        var json = JsonSerializer.Serialize(categories);
        await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        }, ct);

        return Content(json, "application/json");
    }

    // 2. Auto-Suggest Search Bar (Sub-15ms Typeahead) (FRD 2.4 & Arch 4.3)
    [HttpGet("search/suggest")]
    public async Task<IActionResult> AutoSuggest([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(Array.Empty<object>());
        }

        var term = q.Trim();

        var suggestions = await _db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active &&
                        (EF.Functions.ToTsVector("english", p.Title + " " + (p.BrandName ?? ""))
                            .Matches(EF.Functions.PhraseToTsQuery("english", term))
                         || EF.Functions.ILike(p.Title, $"%{term}%")))
            .OrderByDescending(p => p.QualityScore)
            .Take(8)
            .Select(p => new
            {
                p.Title,
                p.Slug,
                p.BrandName,
                CategoryName = p.Category != null ? p.Category.Name : null,
                ThumbnailUrl = p.MediaGallery.OrderBy(m => m.DisplayOrder).Select(m => m.Url).FirstOrDefault(),
                StartingPrice = p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.SellingPrice) ?? 0m
            })
            .ToListAsync(ct);

        return Ok(suggestions);
    }

    // 3. Faceted Product Discovery & Search Engine (FRD 2.4 & Arch 4.3)
    [HttpGet("products")]
    public async Task<IActionResult> SearchProducts([FromQuery] CatalogFilterQuery query, CancellationToken ct)
    {
        var tenant = _tenantContext.CurrentTenant!;
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 50);

        var baseQuery = _db.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active);

        // Keyword Search via PostgreSQL Full-Text Search + ILike fallback
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var searchTerm = query.Q.Trim();
            baseQuery = baseQuery.Where(p =>
                EF.Functions.ToTsVector("english", p.Title + " " + (p.BrandName ?? "") + " " + (p.ShortDescription ?? ""))
                    .Matches(EF.Functions.PlainToTsQuery("english", searchTerm))
                || EF.Functions.ILike(p.Title, $"%{searchTerm}%"));
        }

        // Category Filter
        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            var catSlug = query.CategorySlug.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(p => p.Category != null && p.Category.Slug == catSlug);
        }

        // Brand Filter
        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brand = query.Brand.Trim();
            baseQuery = baseQuery.Where(p => p.BrandName != null && EF.Functions.ILike(p.BrandName, brand));
        }

        // Trust Badge Filter (FRD 2.4)
        if (query.AssuredOnly == true)
        {
            baseQuery = baseQuery.Where(p => p.IsAssuredQualityBadge);
        }

        // Rating Filter (FRD 2.4)
        if (query.MinRating.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.AverageRating >= query.MinRating.Value);
        }

        // Price Range Filter
        if (query.MinPrice.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.Variants.Any(v => v.IsActive && v.SellingPrice >= query.MinPrice.Value));
        }

        if (query.MaxPrice.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.Variants.Any(v => v.IsActive && v.SellingPrice <= query.MaxPrice.Value));
        }

        // Discount % Filter (FRD 2.4)
        if (query.MinDiscount.HasValue && query.MinDiscount.Value > 0)
        {
            decimal minDisc = query.MinDiscount.Value;
            baseQuery = baseQuery.Where(p => p.Variants.Any(v =>
                v.IsActive && v.Mrp > 0 && ((v.Mrp - v.SellingPrice) * 100m / v.Mrp) >= minDisc));
        }

        // In-Stock Availability Filter (FRD 2.4 & 3.4)
        if (query.InStockOnly == true)
        {
            baseQuery = baseQuery.Where(p => p.Variants.Any(v => v.IsActive && (v.StockQty - v.BufferStockQty) > 0));
        }

        // Sorting
        baseQuery = query.SortBy.ToLowerInvariant() switch
        {
            "price_asc" => baseQuery.OrderBy(p => p.Variants.Where(v => v.IsActive).Min(v => v.SellingPrice)),
            "price_desc" => baseQuery.OrderByDescending(p => p.Variants.Where(v => v.IsActive).Min(v => v.SellingPrice)),
            "rating" => baseQuery.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount),
            _ => baseQuery.OrderByDescending(p => p.CreatedAt)
        };

        int totalCount = await baseQuery.CountAsync(ct);

        int baseDeliveryDays = 4 + (tenant.IsHolidayMode ? tenant.HolidayExtraDeliveryDays : 0);

        var items = await baseQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.BrandName,
                CategoryName = p.Category != null ? p.Category.Name : null,
                CategorySlug = p.Category != null ? p.Category.Slug : null,
                p.AverageRating,
                p.ReviewCount,
                p.IsAssuredQualityBadge,
                p.ReplacementDays,
                ThumbnailUrl = p.MediaGallery.OrderBy(m => m.DisplayOrder).Select(m => m.Url).FirstOrDefault(),
                Mrp = p.Variants.Where(v => v.IsActive).OrderBy(v => v.SellingPrice).Select(v => v.Mrp).FirstOrDefault(),
                SellingPrice = p.Variants.Where(v => v.IsActive).OrderBy(v => v.SellingPrice).Select(v => v.SellingPrice).FirstOrDefault(),
                DiscountPercentage = p.Variants.Where(v => v.IsActive).OrderBy(v => v.SellingPrice)
                    .Select(v => v.Mrp > 0 ? Math.Round((v.Mrp - v.SellingPrice) * 100m / v.Mrp, 0) : 0m)
                    .FirstOrDefault(),
                TotalSellableStock = p.Variants.Where(v => v.IsActive).Sum(v => Math.Max(0, v.StockQty - v.BufferStockQty)),
                EstimatedDeliveryDays = baseDeliveryDays
            })
            .ToListAsync(ct);

        return Ok(new
        {
            totalCount,
            page,
            pageSize,
            isStoreOnHoliday = tenant.IsHolidayMode,
            holidayExtraDeliveryDays = tenant.HolidayExtraDeliveryDays,
            items
        });
    }

    // 4. Redis-Cached Product Detail Page (PDP) (FRD 2.4 & Arch 4.2)
    [HttpGet("products/{slug}")]
    public async Task<IActionResult> GetProductDetail(string slug, CancellationToken ct)
    {
        var tenant = _tenantContext.CurrentTenant!;
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var cacheKey = $"tenant:{tenant.TenantId}:pdp:{normalizedSlug}";

        var cachedPdp = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cachedPdp))
        {
            return Content(cachedPdp, "application/json");
        }

        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants.Where(v => v.IsActive))
            .Include(p => p.MediaGallery.OrderBy(m => m.DisplayOrder))
            .FirstOrDefaultAsync(p => p.Slug == normalizedSlug && p.Status == ProductStatus.Active, ct);

        if (product is null)
        {
            return NotFound(new { error = "Product not found or currently unavailable." });
        }

        int estimatedDeliveryDays = 4 + (tenant.IsHolidayMode ? tenant.HolidayExtraDeliveryDays : 0);

        var pdpResponse = new
        {
            product.Id,
            product.Title,
            product.Slug,
            product.BrandName,
            product.ShortDescription,
            product.RichDescriptionHtml,
            product.HsnCode,
            product.GstPercentage,
            product.BulletPoints,
            product.SeoKeywords,
            product.QuantityDiscounts,
            product.APlusContentJson,
            product.AverageRating,
            product.ReviewCount,
            Category = product.Category is null ? null : new { product.Category.Id, product.Category.Name, product.Category.Slug },
            TrustSignals = new
            {
                IsVerifiedStore = true,
                product.IsAssuredQualityBadge,
                Is100PercentAuthentic = true,
                ReplacementPolicyText = $"{product.ReplacementDays}-Day Easy Replacement"
            },
            DeliveryEstimate = new
            {
                IsHolidayMode = tenant.IsHolidayMode,
                ExtraHolidayDays = tenant.HolidayExtraDeliveryDays,
                EstimatedDeliveryDays = estimatedDeliveryDays,
                EstimatedArrivalDate = DateTimeOffset.UtcNow.AddDays(estimatedDeliveryDays).ToString("ddd, dd MMM yyyy")
            },
            MediaGallery = product.MediaGallery.Select(m => new
            {
                m.Id,
                MediaType = m.MediaType.ToString(),
                m.Url,
                m.ThumbnailUrl,
                m.AltText,
                m.DisplayOrder
            }),
            Variants = product.Variants.OrderByDescending(v => v.IsDefault).ThenBy(v => v.SellingPrice).Select(v =>
            {
                int sellable = Math.Max(0, v.StockQty - v.BufferStockQty);
                return new
                {
                    v.Id,
                    v.Sku,
                    v.Size,
                    v.Color,
                    v.Material,
                    v.PackSize,
                    v.Mrp,
                    v.SellingPrice,
                    DiscountPercentage = v.Mrp > 0 ? Math.Round((v.Mrp - v.SellingPrice) * 100m / v.Mrp, 0) : 0m,
                    SellableStock = sellable,
                    StockBadge = sellable == 0 ? "Out of Stock" : (sellable <= v.LowStockAlertThreshold ? $"Only {sellable} left!" : "In Stock"),
                    v.IsDefault
                };
            })
        };

        var json = JsonSerializer.Serialize(pdpResponse);
        await _cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        }, ct);

        return Content(json, "application/json");
    }

    // 5. WhatsApp, Instagram & Social SEO OpenGraph HTML Renderer (Arch 4.2)
    [HttpGet("seo/products/{slug}")]
    public async Task<IActionResult> GetSocialSeoPreviewHtml(string slug, CancellationToken ct)
    {
        var tenant = _tenantContext.CurrentTenant!;
        var normalizedSlug = slug.Trim().ToLowerInvariant();

        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Slug == normalizedSlug && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Title,
                p.ShortDescription,
                p.BrandName,
                ThumbnailUrl = p.MediaGallery.OrderBy(m => m.DisplayOrder).Select(m => m.Url).FirstOrDefault(),
                Price = p.Variants.Where(v => v.IsActive).OrderBy(v => v.SellingPrice).Select(v => v.SellingPrice).FirstOrDefault(),
                InStock = p.Variants.Any(v => v.IsActive && (v.StockQty - v.BufferStockQty) > 0)
            })
            .FirstOrDefaultAsync(ct);

        if (product is null) return NotFound();

        string safeTitle = WebUtility.HtmlEncode($"{product.Title} | {tenant.StoreName}");
        string safeDesc = WebUtility.HtmlEncode(product.ShortDescription ?? $"Shop {product.Title} at ₹{product.Price} on {tenant.StoreName}.");
        string safeImg = WebUtility.HtmlEncode(product.ThumbnailUrl ?? "");
        string domain = tenant.CustomDomain ?? $"{tenant.Subdomain}.yourplatform.com";
        string canonicalUrl = $"https://{domain}/products/{normalizedSlug}";
        string availability = product.InStock ? "instock" : "oos";

        string html = $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <title>{safeTitle}</title>
            <meta name="description" content="{safeDesc}" />
            <link rel="canonical" href="{canonicalUrl}" />
            <!-- OpenGraph / WhatsApp / Instagram / Facebook -->
            <meta property="og:type" content="product" />
            <meta property="og:site_name" content="{WebUtility.HtmlEncode(tenant.StoreName)}" />
            <meta property="og:title" content="{safeTitle}" />
            <meta property="og:description" content="{safeDesc}" />
            <meta property="og:image" content="{safeImg}" />
            <meta property="og:url" content="{canonicalUrl}" />
            <meta property="product:price:amount" content="{product.Price:0.00}" />
            <meta property="product:price:currency" content="INR" />
            <meta property="product:availability" content="{availability}" />
            <!-- Twitter Card -->
            <meta name="twitter:card" content="summary_large_image" />
            <meta name="twitter:title" content="{safeTitle}" />
            <meta name="twitter:description" content="{safeDesc}" />
            <meta name="twitter:image" content="{safeImg}" />
        </head>
        <body>
            <h1>{safeTitle}</h1>
            <p>{safeDesc}</p>
            <p>Price: ₹{product.Price:0.00}</p>
        </body>
        </html>
        """;

        return Content(html, "text/html; charset=utf-8");
    }
}