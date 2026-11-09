using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;

namespace SaasEcommerce.Infrastructure.Invoices;

public sealed class QuestPdfInvoiceService : IInvoicePdfService
{
    public QuestPdfInvoiceService()
    {
        // Configure QuestPDF Community License (100% Free for small businesses)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateGstInvoicePdf(Order order, Tenant tenant)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily("Helvetica"));

                // 1. Header
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(tenant.LegalBusinessName ?? tenant.StoreName)
                            .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                        col.Item().Text($"Storefront: {tenant.CustomDomain ?? tenant.Subdomain}");
                        col.Item().Text($"GSTIN: {tenant.Gstin ?? "UNREGISTERED / COMPOSITION"}");
                        col.Item().Text($"Origin State Code: {tenant.RegisteredStateCode ?? "07"}");
                    });

                    row.ConstantItem(180).AlignRight().Column(col =>
                    {
                        col.Item().Text("TAX INVOICE").FontSize(16).Bold().FontColor(Colors.Grey.Darken3);
                        col.Item().Text($"Invoice #: {order.InvoiceNumber}").Bold();
                        col.Item().Text($"Order #: {order.OrderNumber}");
                        col.Item().Text($"Date: {order.CreatedAt:dd MMM yyyy}");
                        col.Item().Text($"Payment: {order.PaymentMethod} ({order.PaymentStatus})");
                    });
                });

                // 2. Content Body
                page.Content().PaddingVertical(15).Column(col =>
                {
                    // Bill To / Ship To Box
                    col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BILLED & SHIPPED TO:").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(order.ShippingRecipientName).Bold().FontSize(11);
                            c.Item().Text(order.ShippingAddressFull);
                            c.Item().Text($"{order.ShippingState} - {order.ShippingPinCode} (State Code: {order.ShippingStateCode})");
                            c.Item().Text($"Phone: {order.ShippingPhone}");
                        });
                    });

                    col.Item().PaddingTop(12);

                    // Line Items Table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3.2f); // Item & SKU
                            columns.ConstantColumn(52);   // HSN
                            columns.ConstantColumn(30);   // Qty
                            columns.ConstantColumn(60);   // Taxable
                            columns.ConstantColumn(45);   // GST %
                            columns.ConstantColumn(65);   // Tax Amt
                            columns.ConstantColumn(65);   // Total
                        });

                        table.Header(header =>
                        {
                            string[] headers = ["Description / SKU", "HSN", "Qty", "Taxable", "GST %", "GST Amt", "Total (INR)"];
                            foreach (var h in headers)
                            {
                                header.Cell().Background(Colors.Blue.Darken3).Padding(5)
                                    .Text(h).FontColor(Colors.White).Bold().FontSize(8.5f);
                            }
                        });

                        foreach (var item in order.Items)
                        {
                            decimal itemTax = item.CgstAmount + item.SgstAmount + item.IgstAmount;
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5)
                                .Text($"{item.ProductTitle}\nSKU: {item.Sku} {item.VariantAttributes}").FontSize(8.5f);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(item.HsnCode);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(item.Quantity.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"Rs. {item.TaxableAmount:0.00}");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"{item.GstPercentage:0.##}%");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"Rs. {itemTax:0.00}");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"Rs. {item.LineTotal:0.00}").Bold();
                        }
                    });

                    // Summary Totals
                    col.Item().PaddingTop(12).AlignRight().Width(240).Column(tot =>
                    {
                        tot.Item().Row(r => { r.RelativeItem().Text("Taxable Subtotal:"); r.ConstantItem(90).AlignRight().Text($"Rs. {order.SubTotalTaxable:0.00}"); });
                        if (order.TotalIgst > 0)
                        {
                            tot.Item().Row(r => { r.RelativeItem().Text("IGST (Inter-State):"); r.ConstantItem(90).AlignRight().Text($"Rs. {order.TotalIgst:0.00}"); });
                        }
                        else
                        {
                            tot.Item().Row(r => { r.RelativeItem().Text("CGST (Intra-State):"); r.ConstantItem(90).AlignRight().Text($"Rs. {order.TotalCgst:0.00}"); });
                            tot.Item().Row(r => { r.RelativeItem().Text("SGST (Intra-State):"); r.ConstantItem(90).AlignRight().Text($"Rs. {order.TotalSgst:0.00}"); });
                        }

                        if (order.DiscountAmount > 0)
                        {
                            tot.Item().Row(r => { r.RelativeItem().Text($"Discount ({order.AppliedCouponCode}):"); r.ConstantItem(90).AlignRight().Text($"- Rs. {order.DiscountAmount:0.00}"); });
                        }

                        tot.Item().Row(r => { r.RelativeItem().Text("Shipping Charges:"); r.ConstantItem(90).AlignRight().Text($"Rs. {order.ShippingCharge:0.00}"); });
                        tot.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Darken2).Row(r =>
                        {
                            r.RelativeItem().Text("GRAND TOTAL:").Bold().FontSize(11);
                            r.ConstantItem(90).AlignRight().Text($"Rs. {order.GrandTotal:0.00}").Bold().FontSize(11).FontColor(Colors.Blue.Darken3);
                        });
                    });
                });

                // 3. Footer
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span($"Computer generated GST Tax Invoice from {tenant.StoreName} • Authorised Signatory").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }
}