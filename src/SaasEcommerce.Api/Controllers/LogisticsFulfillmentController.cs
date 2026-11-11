using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;
using SaasEcommerce.Infrastructure.Persistence;

namespace SaasEcommerce.Api.Controllers;

public record BookShipmentRequest(string? PreferredCourierName = null);

public record CourierWebhookPayload(
    string AwbNumber,
    ShipmentStatus NewStatus,
    string CurrentLocation,
    string Remarks);

public record CreateRtoDisputeRequest(
    string IssueType,
    string SellerRemarks,
    List<string> ProofMediaUrls);

[ApiController]
public sealed class LogisticsFulfillmentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogisticsService _logistics;

    public LogisticsFulfillmentController(
        AppDbContext db,
        ITenantContext tenantContext,
        ILogisticsService logistics)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logistics = logistics;
    }

    // 1. Public PIN Code Serviceability & Delivery ETA Check (FRD 2.2)
    [HttpGet("api/v1/storefront/serviceability")]
    public IActionResult CheckPinServiceability([FromQuery] string pinCode, [FromQuery] decimal weightGrams = 500m)
    {
        var tenant = _tenantContext.CurrentTenant!;
        var result = _logistics.CheckPinServiceability(
            originPin: "110001",
            destinationPin: pinCode ?? string.Empty,
            weightGrams: weightGrams,
            isHolidayMode: tenant.IsHolidayMode,
            holidayExtraDays: tenant.HolidayExtraDeliveryDays);

        if (!result.IsServiceable)
        {
            return BadRequest(new { error = "PIN code is invalid or currently non-serviceable.", result });
        }

        return Ok(result);
    }

    // 2. Seller 1-Click Order Fulfillment & AWB Courier Booking (FRD 3.6)
    [HttpPost("api/v1/seller/orders/{orderId:guid}/ship")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> BookShipmentAndGenerateAwb(
        Guid orderId,
        [FromBody] BookShipmentRequest req,
        CancellationToken ct)
    {
        var tenant = _tenantContext.CurrentTenant!;

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null) return NotFound(new { error = "Order not found." });

        if (order.OrderStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "Cannot ship a cancelled order." });

        var existingShipment = await _db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId, ct);
        if (existingShipment is not null)
        {
            return Conflict(new { error = "Shipment already booked for this order.", existingShipment.AwbNumber });
        }

        // Calculate total physical vs volumetric weight from variants
        var variantIds = order.Items.Select(i => i.ProductVariantId).ToList();
        var variants = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        decimal totalDeadWeight = 0m;
        decimal maxL = 15m, maxW = 15m, totalH = 0m;

        foreach (var item in order.Items)
        {
            if (variants.TryGetValue(item.ProductVariantId, out var v))
            {
                totalDeadWeight += v.WeightGrams * item.Quantity;
                maxL = Math.Max(maxL, v.LengthCm);
                maxW = Math.Max(maxW, v.WidthCm);
                totalH += v.HeightCm * item.Quantity;
            }
            else
            {
                totalDeadWeight += 500m * item.Quantity;
                totalH += 5m * item.Quantity;
            }
        }

        // Volumetric weight in grams = (L * W * H / 5000) * 1000
        decimal volumetricWeightGrams = Math.Round((maxL * maxW * totalH) / 5m, 2);
        decimal chargeableWeight = Math.Max(totalDeadWeight, volumetricWeightGrams);

        var serviceability = _logistics.CheckPinServiceability(
            "110001", order.ShippingPinCode, chargeableWeight, tenant.IsHolidayMode, tenant.HolidayExtraDeliveryDays);

        string courierName = !string.IsNullOrWhiteSpace(req.PreferredCourierName)
            ? req.PreferredCourierName.Trim()
            : serviceability.RecommendedCourier;

        string awbNumber = $"AWB{DateTime.UtcNow:yyMMdd}{Random.Shared.Next(100000, 999999)}";
        string domain = tenant.CustomDomain ?? $"{tenant.Subdomain}.yourplatform.com";

        var shipment = new Shipment
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CourierName = courierName,
            AwbNumber = awbNumber,
            ExternalShipmentId = $"SR-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            ChargeableWeightGrams = chargeableWeight,
            LengthCm = maxL,
            WidthCm = maxW,
            HeightCm = Math.Max(5m, totalH),
            EstimatedFreightCost = serviceability.EstimatedShippingRateInr,
            Status = ShipmentStatus.Manifested,
            LiveTrackingUrl = $"https://{domain}/track/{awbNumber}",
            Checkpoints = new List<TrackingCheckpoint>
            {
                new(
                    Status: nameof(ShipmentStatus.Manifested),
                    Location: "Seller Warehouse",
                    Remarks: $"Shipment manifested with {courierName}. AWB {awbNumber} generated.",
                    Timestamp: DateTimeOffset.UtcNow)
            }
        };

        order.OrderStatus = OrderStatus.ReadyToShip;

        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetBuyerOrderTracking), new { orderId = order.Id }, new
        {
            shipment.Id,
            shipment.OrderId,
            shipment.OrderNumber,
            OrderStatus = order.OrderStatus.ToString(),
            shipment.CourierName,
            shipment.AwbNumber,
            shipment.ChargeableWeightGrams,
            shipment.EstimatedFreightCost,
            shipment.LiveTrackingUrl,
            shipment.Checkpoints
        });
    }

    // 3. Download 4x6 Barcoded Thermal Shipping Label PDF (FRD 3.6)
    [HttpGet("api/v1/seller/orders/{orderId:guid}/shipping-label")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> DownloadShippingLabel(Guid orderId, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null) return NotFound(new { error = "Order not found." });

        var shipment = await _db.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == orderId, ct);

        if (shipment is null) return BadRequest(new { error = "Please book the shipment first to generate an AWB label." });

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct);
        byte[] pdfBytes = _logistics.GenerateThermalShippingLabelPdf(order, shipment, tenant);

        return File(pdfBytes, "application/pdf", $"Label-{shipment.AwbNumber}.pdf");
    }

    // 4. Download Daily Courier Handover Manifest PDF (FRD 3.6)
    [HttpGet("api/v1/seller/shipments/manifest")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> DownloadCourierManifest(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var shipments = await _db.Shipments
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, ct);
        byte[] pdfBytes = _logistics.GenerateCourierManifestPdf(shipments, tenant);

        return File(pdfBytes, "application/pdf", $"Manifest-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    // 5. Courier Live Tracking Webhook Receiver (FRD 2.6 & 3.6)
    [HttpPost("api/v1/webhooks/courier/tracking")]
    public async Task<IActionResult> ReceiveCourierTrackingWebhook(
        [FromBody] CourierWebhookPayload payload,
        CancellationToken ct)
    {
        var awb = payload.AwbNumber.Trim().ToUpperInvariant();
        var shipment = await _db.Shipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.AwbNumber == awb, ct);

        if (shipment is null) return NotFound(new { error = $"AWB '{awb}' not found for this store." });

        shipment.Status = payload.NewStatus;
        shipment.Checkpoints.Add(new TrackingCheckpoint(
            Status: payload.NewStatus.ToString(),
            Location: payload.CurrentLocation,
            Remarks: payload.Remarks,
            Timestamp: DateTimeOffset.UtcNow));

        // Explicitly mark JSONB property as modified so EF Core persists the new checkpoint
        _db.Entry(shipment).Property(x => x.Checkpoints).IsModified = true;

        if (payload.NewStatus == ShipmentStatus.PickedUp)
        {
            shipment.PickedUpAt ??= DateTimeOffset.UtcNow;
            if (shipment.Order is not null) shipment.Order.OrderStatus = OrderStatus.Shipped;
        }
        else if (payload.NewStatus == ShipmentStatus.InTransit)
        {
            if (shipment.Order is not null) shipment.Order.OrderStatus = OrderStatus.Shipped;
        }
        else if (payload.NewStatus == ShipmentStatus.OutForDelivery)
        {
            if (shipment.Order is not null) shipment.Order.OrderStatus = OrderStatus.OutForDelivery;
        }
        else if (payload.NewStatus == ShipmentStatus.Delivered)
        {
            shipment.DeliveredAt = DateTimeOffset.UtcNow;
            if (shipment.Order is not null)
            {
                shipment.Order.OrderStatus = OrderStatus.Delivered;
                if (shipment.Order.PaymentMethod == PaymentMethod.CashOnDelivery)
                {
                    shipment.Order.PaymentStatus = PaymentStatus.Paid;
                    shipment.Order.PaidAt = DateTimeOffset.UtcNow;
                }
            }
        }
        else if (payload.NewStatus is ShipmentStatus.RtoInitiated or ShipmentStatus.RtoDelivered)
        {
            if (shipment.Order is not null) shipment.Order.OrderStatus = OrderStatus.Returned;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            shipment.AwbNumber,
            ShipmentStatus = shipment.Status.ToString(),
            OrderStatus = shipment.Order?.OrderStatus.ToString(),
            TotalCheckpoints = shipment.Checkpoints.Count,
            LatestCheckpoint = shipment.Checkpoints.Last()
        });
    }

    // 6. Buyer Live Shipment Tracking Timeline (FRD 2.6)
    [HttpGet("api/v1/buyer/orders/{orderId:guid}/tracking")]
    [Authorize(Policy = "BuyerOrSeller")]
    public async Task<IActionResult> GetBuyerOrderTracking(Guid orderId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userRole = User.FindFirstValue(ClaimTypes.Role);

        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return NotFound(new { error = "Order not found." });

        if (userRole == nameof(UserRole.Buyer) && order.BuyerId != userId)
            return Forbid();

        var shipment = await _db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == orderId, ct);
        if (shipment is null)
        {
            return Ok(new
            {
                order.OrderNumber,
                OrderStatus = order.OrderStatus.ToString(),
                Message = "Order is confirmed and being prepared for dispatch.",
                Checkpoints = Array.Empty<object>()
            });
        }

        return Ok(new
        {
            order.OrderNumber,
            OrderStatus = order.OrderStatus.ToString(),
            shipment.CourierName,
            shipment.AwbNumber,
            ShipmentStatus = shipment.Status.ToString(),
            shipment.LiveTrackingUrl,
            shipment.PickedUpAt,
            shipment.DeliveredAt,
            Timeline = shipment.Checkpoints.OrderByDescending(c => c.Timestamp)
        });
    }

    // 7. Log Damaged Return / RTO Dispute Claim with Unboxing Media (FRD 3.6)
    [HttpPost("api/v1/seller/shipments/{shipmentId:guid}/rto-dispute")]
    [Authorize(Policy = "SellerOnly")]
    public async Task<IActionResult> LogRtoDispute(
        Guid shipmentId,
        [FromBody] CreateRtoDisputeRequest req,
        CancellationToken ct)
    {
        var shipment = await _db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null) return NotFound(new { error = "Shipment not found." });

        var claim = new RtoDisputeClaim
        {
            OrderId = shipment.OrderId,
            ShipmentId = shipment.Id,
            AwbNumber = shipment.AwbNumber,
            IssueType = req.IssueType.Trim(),
            SellerRemarks = req.SellerRemarks.Trim(),
            ProofMediaUrls = req.ProofMediaUrls ?? new(),
            Status = RtoDisputeStatus.Submitted
        };

        _db.RtoDisputeClaims.Add(claim);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(LogRtoDispute), claim);
    }
}