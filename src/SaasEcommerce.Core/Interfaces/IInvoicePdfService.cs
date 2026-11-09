using SaasEcommerce.Core.Entities;

namespace SaasEcommerce.Core.Interfaces;

public interface IInvoicePdfService
{
    byte[] GenerateGstInvoicePdf(Order order, Tenant tenant);
}