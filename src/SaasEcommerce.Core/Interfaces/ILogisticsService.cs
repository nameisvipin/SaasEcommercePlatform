using SaasEcommerce.Core.Entities;

namespace SaasEcommerce.Core.Interfaces;

public record PinServiceabilityResult(
    string OriginPinCode,
    string DestinationPinCode,
    bool IsServiceable,
    bool IsCodAvailable,
    int EstimatedDeliveryDays,
    string EstimatedArrivalDate,
    decimal EstimatedShippingRateInr,
    string RecommendedCourier);

public interface ILogisticsService
{
    PinServiceabilityResult CheckPinServiceability(
        string originPin,
        string destinationPin,
        decimal weightGrams,
        bool isHolidayMode,
        int holidayExtraDays);

    byte[] GenerateThermalShippingLabelPdf(Order order, Shipment shipment, Tenant tenant);
    byte[] GenerateCourierManifestPdf(IReadOnlyList<Shipment> shipments, Tenant tenant);
}