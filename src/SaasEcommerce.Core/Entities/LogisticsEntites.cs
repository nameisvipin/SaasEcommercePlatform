using SaasEcommerce.Core.Common;

namespace SaasEcommerce.Core.Entities;

public enum ShipmentStatus
{
    Manifested = 1,      // AWB assigned, label generated, waiting for pickup
    PickedUp = 2,        // Picked up by courier
    InTransit = 3,       // Moving through courier hubs
    OutForDelivery = 4,  // Out with delivery agent
    Delivered = 5,       // Delivered to buyer
    RtoInitiated = 6,    // Buyer rejected / undeliverable -> returning to seller
    RtoDelivered = 7,    // Returned back to seller warehouse
    Cancelled = 8
}

public record TrackingCheckpoint(
    string Status,
    string Location,
    string Remarks,
    DateTimeOffset Timestamp);

// 1. Courier Shipment & Live Tracking Timeline (FRD 2.6 & 3.6)
public class Shipment : TenantScopedEntity
{
    public Guid OrderId { get; set; }
    public required string OrderNumber { get; set; }
    
    public required string CourierName { get; set; } // E.g., "Delhivery Surface", "BlueDart Express"
    public required string AwbNumber { get; set; }   // Barcoded Air Waybill Tracking Number
    public string? ExternalShipmentId { get; set; }  // Shiprocket / NimbusPost Shipment ID
    
    // Calculated Package Weight & Dimensions
    public decimal ChargeableWeightGrams { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal EstimatedFreightCost { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Manifested;
    public string? LiveTrackingUrl { get; set; }
    
    /// <summary>
    /// JSONB chronological list of courier scan checkpoints (FRD 2.6)
    /// </summary>
    public List<TrackingCheckpoint> Checkpoints { get; set; } = new();

    public DateTimeOffset? PickedUpAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public Order? Order { get; set; }
}

public enum RtoDisputeStatus
{
    Submitted = 1,
    UnderReview = 2,
    ApprovedClaim = 3,
    Rejected = 4
}

// 2. Return-to-Origin (RTO) & Damaged Return Dispute Claims (FRD 3.6)
public class RtoDisputeClaim : TenantScopedEntity
{
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public required string AwbNumber { get; set; }
    public required string IssueType { get; set; } // E.g., "Damaged Item Received", "Wrong Product Returned", "Missing Accessories"
    public required string SellerRemarks { get; set; }
    
    /// <summary>
    /// JSONB list of Cloudflare R2 URLs for unboxing photos & videos (FRD 3.6)
    /// </summary>
    public List<string> ProofMediaUrls { get; set; } = new();

    public RtoDisputeStatus Status { get; set; } = RtoDisputeStatus.Submitted;
    public string? AdminResolutionNotes { get; set; }
}