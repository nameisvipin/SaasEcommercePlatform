using SaasEcommerce.Core.Common;

namespace SaasEcommerce.Core.Entities;

public enum AddressTag
{
    Home = 1,
    Work = 2,
    Other = 3
}

// 1. Buyer Multi-Address Book (FRD 2.2)
public class BuyerAddress : TenantScopedEntity
{
    public Guid UserId { get; set; }
    public required string RecipientName { get; set; }
    public required string PhoneNumber { get; set; }
    public required string AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Landmark { get; set; }
    public required string PinCode { get; set; } // 6-digit Indian PIN code
    public required string City { get; set; }
    public required string State { get; set; }
    public required string StateCode { get; set; } // 2-digit GST State Code (e.g., "07" Delhi, "27" Maharashtra)
    public AddressTag Tag { get; set; } = AddressTag.Home;
    public bool IsDefaultShipping { get; set; } = false;
    public bool IsDefaultBilling { get; set; } = false;
}

// 2. Shopping Cart & "Save For Later" Wishlist Items (FRD 2.5)
public class CartItem : TenantScopedEntity
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public int Quantity { get; set; } = 1;
    
    /// <summary>
    /// When true, item is in "Save for Later" and excluded from immediate checkout (FRD 2.5)
    /// </summary>
    public bool IsSavedForLater { get; set; } = false;

    public Product? Product { get; set; }
    public ProductVariant? ProductVariant { get; set; }
}

// 3. Coupons & Promo Engine (FRD 2.5 & 3.3)
public enum DiscountType
{
    Percentage = 1,
    FlatAmount = 2,
    FreeShipping = 3
}

public class Coupon : TenantScopedEntity
{
    public required string Code { get; set; } // E.g., "FEST20"
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public decimal MinOrderAmount { get; set; } = 0m;
    public decimal? MaxDiscountCap { get; set; }
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMonths(1);
    public int? UsageLimitTotal { get; set; }
    public int UsedCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;
}

// 4. Orders & GST Tax Invoicing (FRD 2.5, 2.6, 3.6)
public enum PaymentMethod
{
    OnlineRazorpay = 1,
    CashOnDelivery = 2
}

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4
}

public enum OrderStatus
{
    Ordered = 1,
    ReadyToShip = 2,
    Shipped = 3,
    OutForDelivery = 4,
    Delivered = 5,
    Cancelled = 6,
    Returned = 7
}

public class Order : TenantScopedEntity
{
    public required string OrderNumber { get; set; } // E.g., ORD-20261109-4821
    public required string InvoiceNumber { get; set; } // E.g., INV-2026-00001
    public Guid BuyerId { get; set; }
    
    // Snapshot of Shipping Address at time of order
    public required string ShippingRecipientName { get; set; }
    public required string ShippingPhone { get; set; }
    public required string ShippingAddressFull { get; set; }
    public required string ShippingPinCode { get; set; }
    public required string ShippingState { get; set; }
    public required string ShippingStateCode { get; set; }

    // Financial Breakdown
    public decimal SubTotalTaxable { get; set; }
    public decimal TotalCgst { get; set; }
    public decimal TotalSgst { get; set; }
    public decimal TotalIgst { get; set; }
    public decimal TotalGst { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? AppliedCouponCode { get; set; }
    public decimal ShippingCharge { get; set; }
    public decimal GrandTotal { get; set; }

    // Payment & Gateway Tracking (BYOK)
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Ordered;
    public string? GatewayOrderId { get; set; } // e.g., order_P8x91...
    public string? GatewayPaymentId { get; set; } // e.g., pay_P8x99...
    public DateTimeOffset? PaidAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem : TenantScopedEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public required string ProductTitle { get; set; }
    public required string Sku { get; set; }
    public string? VariantAttributes { get; set; } // E.g., "Size: L | Color: Jet Black"
    public required string HsnCode { get; set; }
    
    public int Quantity { get; set; }
    public decimal UnitPriceInclusive { get; set; } // Selling Price (GST inclusive)
    public decimal TaxableAmount { get; set; }
    public decimal GstPercentage { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }

    public Order? Order { get; set; }
}