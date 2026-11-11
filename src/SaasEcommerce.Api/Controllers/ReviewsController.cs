using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Controllers;

public record CreateReviewRequest(
    Guid ProductId,
    int Rating,
    string Title,
    string Comment,
    List<string>? MediaUrls);

public record SellerReplyRequest(string ReplyText);

[ApiController]
public sealed class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedCache _cache;

    public ReviewsController(
        AppDbContext db,
        ITenantContext tenantContext,
        IDistributedCache cache)
    {
        _db = db;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    // 1. Submit Buyer Review with Automatic Purchase Verification & Rating Aggregation
    [HttpPost("api/v1/buyer/reviews")]
    [Authorize(Policy = "BuyerOrSeller")]
    public async Task<IActionResult> SubmitReview([FromBody] CreateReviewRequest req, CancellationToken ct)
    {
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { error = "Rating must be between 1 and 5 stars." });

        var buyerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var tenantId = _tenantContext.TenantId!.Value;

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId, ct);
        if (product is null) return NotFound(new { error = "Product not found." });

        if (await _db.ProductReviews.AnyAsync(r => r.ProductId == req.ProductId && r.BuyerId == buyerId, ct))
        {
            return Conflict(new { error = "You have already reviewed this product." });
        }

        var buyer = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == buyerId, ct);

        // Verify if buyer actually ordered this product on this storefront
        var matchingOrder = await _db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId &&
                        o.OrderStatus != OrderStatus.Cancelled &&
                        o.Items.Any(i => i.ProductId == req.ProductId))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new { o.Id })
            .FirstOrDefaultAsync(ct);

        var review = new ProductReview
        {
            ProductId = req.ProductId,
            BuyerId = buyerId,
            OrderId = matchingOrder?.Id,
            BuyerName = buyer.FullName,
            Rating = req.Rating,
            Title = req.Title.Trim(),
            Comment = req.Comment.Trim(),
            IsVerifiedPurchase = matchingOrder is not null,
            MediaUrls = req.MediaUrls ?? new(),
            IsApproved = true
        };

        _db.ProductReviews.Add(review);
        await _db.SaveChangesAsync(ct);

        // Recalculate Product AverageRating & ReviewCount
        var stats = await _db.ProductReviews
            .Where(r => r.ProductId == req.ProductId && r.IsApproved)
            .GroupBy(r => r.ProductId)
            .Select(g => new
            {
                Count = g.Count(),
                Average = g.Average(r => (decimal)r.Rating)
            })
            .FirstOrDefaultAsync(ct);

        product.ReviewCount = stats?.Count ?? 1;
        product.AverageRating = Math.Round(stats?.Average ?? req.Rating, 2);

        await _db.SaveChangesAsync(ct);

        // Purge cached PDP in Redis so new rating shows immediately
        await _cache.RemoveAsync($"tenant:{tenantId}:pdp:{product.Slug}", ct);

        return CreatedAtAction(nameof(GetProductReviews), new { productId = product.Id }, new
        {
            review.Id,
            review.ProductId,
            review.BuyerName,
            review.Rating,
            review.Title,
            review.Comment,
            review.IsVerifiedPurchase,
            review.MediaUrls,
            UpdatedProductRating = product.AverageRating,
            UpdatedReviewCount = product.ReviewCount
        });
    }

    // 2. Public Paginated Product Reviews & Star Breakdown
    [HttpGet("api/v1/storefront/products/{productId:guid}/reviews")]
    public async Task<IActionResult> GetProductReviews(Guid productId, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        int pageSize = 10;
        var baseQuery = _db.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.IsApproved);

        var starBreakdown = await baseQuery
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var reviews = await baseQuery
            .OrderByDescending(r => r.IsVerifiedPurchase)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.BuyerName,
                r.Rating,
                r.Title,
                r.Comment,
                r.IsVerifiedPurchase,
                r.MediaUrls,
                r.SellerReply,
                r.SellerRepliedAt,
                r.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new
        {
            productId,
            totalReviews = starBreakdown.Sum(s => s.Count),
            starDistribution = Enumerable.Range(1, 5).OrderByDescending(star => star).Select(star => new
            {
                stars = star,
                count = starBreakdown.FirstOrDefault(x => x.Rating == star)?.Count ?? 0
            }),
            reviews
        });
    }

    // 3. Seller Official Reply to a Buyer Review
    [HttpPatch("api/v1/seller/reviews/{reviewId:guid}/reply")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> ReplyToReview(Guid reviewId, [FromBody] SellerReplyRequest req, CancellationToken ct)
    {
        var review = await _db.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null) return NotFound(new { error = "Review not found." });

        review.SellerReply = req.ReplyText.Trim();
        review.SellerRepliedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            review.Id,
            review.SellerReply,
            review.SellerRepliedAt
        });
    }
}