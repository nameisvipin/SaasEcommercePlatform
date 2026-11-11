using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Controllers;

public record UpdateTenantStatusRequest(TenantStatus Status);
public record ModerateProductRequest(bool IsAssuredQualityBadge, ProductStatus Status);

[ApiController]
public sealed class AnalyticsAndGovernanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedCache _cache;

    public AnalyticsAndGovernanceController(
        AppDbContext db,
        ITenantContext tenantContext,
        IDistributedCache cache)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    // 1. Seller Business, GST & Inventory Analytics Dashboard
    [HttpGet("api/v1/seller/analytics/dashboard")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> GetSellerDashboard(CancellationToken ct)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OrderStatus != OrderStatus.Cancelled)
            .ToListAsync(ct);

        decimal totalGmv = orders.Sum(o => o.GrandTotal);
        decimal netTaxableRevenue = orders.Sum(o => o.SubTotalTaxable);
        decimal totalGstCollected = orders.Sum(o => o.TotalGst);
        decimal totalDiscountsGiven = orders.Sum(o => o.DiscountAmount);

        int totalOrders = orders.Count;
        int paidOnlineCount = orders.Count(o => o.PaymentMethod == PaymentMethod.OnlineRazorpay && o.PaymentStatus == PaymentStatus.Paid);
        int codCount = orders.Count(o => o.PaymentMethod == PaymentMethod.CashOnDelivery);
        int rtoCount = orders.Count(o => o.OrderStatus == OrderStatus.Returned);

        // Top 5 Best-Selling SKUs
        var topSkus = await _db.OrderItems
            .AsNoTracking()
            .GroupBy(i => new { i.Sku, i.ProductTitle })
            .Select(g => new
            {
                g.Key.Sku,
                g.Key.ProductTitle,
                TotalUnitsSold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.TotalUnitsSold)
            .Take(5)
            .ToListAsync(ct);

        // Low-Stock SKU Alerts
        var lowStockAlerts = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && (v.StockQty - v.BufferStockQty) <= v.LowStockAlertThreshold)
            .Select(v => new
            {
                v.Id,
                v.Sku,
                ProductTitle = v.Product != null ? v.Product.Title : "N/A",
                v.Size,
                v.Color,
                v.StockQty,
                v.BufferStockQty,
                SellableStock = Math.Max(0, v.StockQty - v.BufferStockQty),
                v.LowStockAlertThreshold
            })
            .ToListAsync(ct);

        return Ok(new
        {
            financialSummary = new
            {
                totalGmv,
                netTaxableRevenue,
                totalGstCollected,
                totalDiscountsGiven,
                averageOrderValue = totalOrders > 0 ? Math.Round(totalGmv / totalOrders, 2) : 0m
            },
            orderMetrics = new
            {
                totalOrders,
                paidOnlineCount,
                codCount,
                rtoCount,
                rtoPercentage = totalOrders > 0 ? Math.Round((decimal)rtoCount * 100m / totalOrders, 2) : 0m,
                statusBreakdown = orders.GroupBy(o => o.OrderStatus.ToString())
                    .Select(g => new { status = g.Key, count = g.Count() })
            },
            topSellingSkus = topSkus,
            lowStockAlerts
        });
    }

    // 2. Super-Admin Platform-Wide SaaS Overview (All Tenants)
    [HttpGet("api/v1/superadmin/analytics/overview")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> GetPlatformOverview(CancellationToken ct)
    {
        var totalTenants = await _db.Tenants.CountAsync(ct);
        var activeTenants = await _db.Tenants.CountAsync(t => t.Status == TenantStatus.Active, ct);
        var totalOrdersPlatform = await _db.Orders.IgnoreQueryFilters().CountAsync(ct);
        var totalPlatformGmv = await _db.Orders.IgnoreQueryFilters()
            .Where(o => o.OrderStatus != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.GrandTotal, ct) ?? 0m;

        var tenantBreakdown = await _db.Tenants
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.StoreName,
                t.Subdomain,
                t.CustomDomain,
                Status = t.Status.ToString(),
                t.IsHolidayMode,
                ProductCount = _db.Products.IgnoreQueryFilters().Count(p => p.TenantId == t.Id),
                OrderCount = _db.Orders.IgnoreQueryFilters().Count(o => o.TenantId == t.Id),
                StoreGmv = _db.Orders.IgnoreQueryFilters()
                    .Where(o => o.TenantId == t.Id && o.OrderStatus != OrderStatus.Cancelled)
                    .Sum(o => (decimal?)o.GrandTotal) ?? 0m
            })
            .ToListAsync(ct);

        return Ok(new
        {
            totalTenants,
            activeTenants,
            totalOrdersPlatform,
            totalPlatformGmv,
            tenants = tenantBreakdown
        });
    }

    // 3. Super-Admin: Assign "Assured Quality" Badge or Suppress Product Listing
    [HttpPatch("api/v1/superadmin/tenants/{tenantId:guid}/products/{productId:guid}/trust-badge")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> ModerateProductAndAssignBadge(
        Guid tenantId,
        Guid productId,
        [FromBody] ModerateProductRequest req,
        CancellationToken ct)
    {
        var product = await _db.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == productId && p.TenantId == tenantId, ct);

        if (product is null) return NotFound(new { error = "Product not found for this tenant." });

        product.IsAssuredQualityBadge = req.IsAssuredQualityBadge;
        product.Status = req.Status;

        await _db.SaveChangesAsync(ct);

        // Immediately purge Redis PDP cache so Assured Quality badge displays live
        await _cache.RemoveAsync($"tenant:{tenantId}:pdp:{product.Slug}", ct);

        return Ok(new
        {
            product.Id,
            product.TenantId,
            product.Title,
            product.Slug,
            product.IsAssuredQualityBadge,
            Status = product.Status.ToString()
        });
    }

    // 4. Super-Admin: Suspend or Reactivate a Client Storefront
    [HttpPatch("api/v1/superadmin/tenants/{tenantId:guid}/status")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> UpdateTenantStatus(
        Guid tenantId,
        [FromBody] UpdateTenantStatusRequest req,
        CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { error = "Tenant not found." });

        tenant.Status = req.Status;
        await _db.SaveChangesAsync(ct);

        // Immediately purge Redis edge routing cache so suspended stores are blocked in <1ms
        await _cache.RemoveAsync($"tenant:slug:{tenant.Subdomain}", ct);
        if (!string.IsNullOrEmpty(tenant.CustomDomain))
        {
            await _cache.RemoveAsync($"tenant:host:{tenant.CustomDomain}", ct);
        }

        return Ok(new
        {
            tenant.Id,
            tenant.StoreName,
            Status = tenant.Status.ToString(),
            message = $"Tenant status updated to {tenant.Status} and edge cache purged."
        });
    }
}
