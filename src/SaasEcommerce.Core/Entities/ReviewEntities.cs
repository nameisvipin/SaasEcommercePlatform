using SaasEcommerce.Core.Common;

namespace SaasEcommerce.Core.Entities;

public class ProductReview : TenantScopedEntity
{
    public Guid ProductId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid? OrderId { get; set; }

    public required string BuyerName { get; set; }
    public int Rating { get; set; } // 1 to 5 stars
    public required string Title { get; set; }
    public required string Comment { get; set; }

    /// <summary>
    /// Automatically set to true if the Buyer has a non-cancelled Order containing this ProductId
    /// </summary>
    public bool IsVerifiedPurchase { get; set; } = false;

    /// <summary>
    /// JSONB list of Cloudflare R2 URLs for customer unboxing photos/videos
    /// </summary>
    public List<string> MediaUrls { get; set; } = new();

    // Seller Moderation & Response
    public string? SellerReply { get; set; }
    public DateTimeOffset? SellerRepliedAt { get; set; }
    public bool IsApproved { get; set; } = true;

    public Product? Product { get; set; }
}