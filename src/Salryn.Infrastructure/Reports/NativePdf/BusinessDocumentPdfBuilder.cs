using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Services;
using Unit = MigraDoc.DocumentObjectModel.Unit;

namespace Salryn.Infrastructure.Reports.NativePdf;

/// <summary>
/// Generates read-only A4 PDF business documents via PDFsharp/MigraDoc for D07 workflows.
/// These are operational/commercial documents only and never claim BIR accreditation or mutate state.
/// </summary>
public static class BusinessDocumentPdfBuilder
{
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);
    private const double PrintableWidthCm = 18.4;

    static BusinessDocumentPdfBuilder()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public static byte[] BuildPurchaseRequisitionPdf(PurchaseRequisitionModel pr, StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(pr);
        (Document document, Section section) = CreateA4Document($"Purchase Requisition {pr.PrNumber}", store);
        AddDocumentHeader(section, store, "PURCHASE REQUISITION", pr.PrNumber, new[]
        {
            $"Status: {pr.Status}",
            $"Priority: {pr.Priority}",
            $"Requested: {pr.DateRequested:yyyy-MM-dd}",
            $"Needed: {pr.DateNeeded:yyyy-MM-dd}"
        });

        AddMetadataGrid(section, new[]
        {
            ("Department", pr.DepartmentName),
            ("Requested By", pr.RequestedByName),
            ("Approved By", pr.ApprovedByName ?? "Pending Approval"),
            ("Purpose", pr.Purpose)
        });

        Table table = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
        {
            ("#", 0.9, ParagraphAlignment.Center),
            ("Code", 2.4, ParagraphAlignment.Left),
            ("Item Description", 5.5, ParagraphAlignment.Left),
            ("Class", 2.0, ParagraphAlignment.Left),
            ("UOM", 1.3, ParagraphAlignment.Center),
            ("Req Qty", 1.8, ParagraphAlignment.Right),
            ("Appr Qty", 1.8, ParagraphAlignment.Right),
            ("Est. Total", 2.7, ParagraphAlignment.Right)
        });

        foreach (var line in pr.Lines)
        {
            Row row = table.AddRow();
            SetCell(row, 0, line.LineNumber.ToString(CultureInfo.InvariantCulture), ParagraphAlignment.Center);
            SetCell(row, 1, line.ItemCode);
            SetCell(row, 2, line.ItemDescription);
            SetCell(row, 3, line.PurchaseClassification);
            SetCell(row, 4, line.UnitName, ParagraphAlignment.Center);
            SetCell(row, 5, FormatQty(line.RequestedQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 6, FormatQty(line.ApprovedQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 7, FormatMoney(line.EstimatedLineTotalCents), ParagraphAlignment.Right);
        }

        AddTotalsSection(section, new[]
        {
            ("Estimated Total", FormatMoney(pr.EstimatedTotalCents), true)
        });
        AddNotesBlock(section, "Notes", pr.Notes);
        if (!string.IsNullOrWhiteSpace(pr.RejectionOrRevisionReason))
        {
            AddNotesBlock(section, "Review / Decision Remarks", pr.RejectionOrRevisionReason);
        }
        AddSignatories(section, new[]
        {
            ("Prepared By", pr.RequestedByName),
            ("Reviewed / Approved By", pr.ApprovedByName ?? "________________________")
        });

        return RenderPdfBytes(document, $"PR-{pr.PrNumber}");
    }

    public static byte[] BuildPurchaseOrderPdf(PurchaseOrderModel po, StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(po);
        if (po.Lines.Count == 0 || po.TotalAmountCents < 0 || po.SubtotalCents < 0)
        {
            throw new InvalidOperationException("Purchase order financial lines or totals are invalid.");
        }

        (Document document, Section section) = CreateA4Document($"Purchase Order {po.PoNumber}", store);
        AddDocumentHeader(section, store, "PURCHASE ORDER", po.PoNumber, new[]
        {
            $"Status: {po.Status}",
            $"Order Date: {po.OrderDate:yyyy-MM-dd}",
            $"Expected Delivery: {po.ExpectedDeliveryDate:yyyy-MM-dd}",
            $"Payment Terms: {po.PaymentTermsDays} day(s)"
        });

        AddMetadataGrid(section, new[]
        {
            ("Vendor / Supplier", string.IsNullOrWhiteSpace(po.SupplierCode) ? po.SupplierName : $"{po.SupplierName} ({po.SupplierCode})"),
            ("Delivery Destination", po.DeliveryLocationName),
            ("Source Requisition", po.SourcePrNumber ?? "Direct PO"),
            ("Prepared / Approved", $"{po.CreatedByName} / {po.ApprovedByName ?? "Pending"}")
        });

        Table table = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
        {
            ("#", 0.9, ParagraphAlignment.Center),
            ("Code", 2.2, ParagraphAlignment.Left),
            ("Item Description", 5.5, ParagraphAlignment.Left),
            ("UOM", 1.3, ParagraphAlignment.Center),
            ("Qty", 1.8, ParagraphAlignment.Right),
            ("Unit Cost", 2.2, ParagraphAlignment.Right),
            ("Tax / Disc", 2.0, ParagraphAlignment.Right),
            ("Line Total", 2.5, ParagraphAlignment.Right)
        });

        foreach (var line in po.Lines)
        {
            Row row = table.AddRow();
            SetCell(row, 0, line.LineNumber.ToString(CultureInfo.InvariantCulture), ParagraphAlignment.Center);
            SetCell(row, 1, line.ItemCode);
            SetCell(row, 2, line.ItemDescription);
            SetCell(row, 3, line.UnitName, ParagraphAlignment.Center);
            SetCell(row, 4, FormatQty(line.OrderedQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 5, FormatMoney(line.UnitCostCents), ParagraphAlignment.Right);
            SetCell(row, 6, $"{FormatMoney(line.TaxAmountCents)} / {FormatMoney(line.DiscountCents)}", ParagraphAlignment.Right);
            SetCell(row, 7, FormatMoney(line.LineTotalCents), ParagraphAlignment.Right);
        }

        AddTotalsSection(section, new[]
        {
            ("Subtotal", FormatMoney(po.SubtotalCents), false),
            ("Discount", FormatMoney(po.DiscountCents), false),
            ("Tax", FormatMoney(po.TaxCents), false),
            ("Shipping / Freight", FormatMoney(po.ShippingCents), false),
            ("Total PO Amount", FormatMoney(po.TotalAmountCents), true)
        });
        AddNotesBlock(section, "Terms & Conditions", po.TermsAndConditions);
        AddNotesBlock(section, "Notes", po.Notes);
        AddSignatories(section, new[]
        {
            ("Prepared By", po.CreatedByName),
            ("Approved By", po.ApprovedByName ?? "________________________"),
            ("Supplier Acknowledgment", "________________________")
        });

        return RenderPdfBytes(document, $"PO-{po.PoNumber}");
    }

    public static byte[] BuildReceivingReportPdf(ReceivingReportModel rr, StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(rr);
        (Document document, Section section) = CreateA4Document($"Receiving Report {rr.RrNumber}", store);
        AddDocumentHeader(section, store, "GOODS RECEIVING REPORT (RR)", rr.RrNumber, new[]
        {
            $"Status: {rr.Status}{(rr.HasOverdelivery ? " (Overdelivery)" : string.Empty)}",
            $"Received Date: {rr.ReceivedDate:yyyy-MM-dd}",
            $"PO Reference: {rr.PoNumber}",
            $"Delivery Receipt No: {rr.DeliveryReceiptNo}"
        });

        AddMetadataGrid(section, new[]
        {
            ("Supplier", rr.SupplierName),
            ("Receiving Location", rr.LocationName),
            ("Supplier Invoice Ref", rr.SupplierInvoiceRef ?? "None"),
            ("Carrier / Driver", rr.CarrierOrDriver ?? "N/A")
        });

        Table table = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
        {
            ("Code", 2.2, ParagraphAlignment.Left),
            ("Item Description", 4.6, ParagraphAlignment.Left),
            ("UOM", 1.2, ParagraphAlignment.Center),
            ("Ordered", 1.7, ParagraphAlignment.Right),
            ("Deliv.", 1.7, ParagraphAlignment.Right),
            ("Accept", 1.7, ParagraphAlignment.Right),
            ("Reject", 1.5, ParagraphAlignment.Right),
            ("Unit Cost", 1.8, ParagraphAlignment.Right),
            ("Accepted Amt", 2.0, ParagraphAlignment.Right)
        });

        foreach (var line in rr.Lines)
        {
            Row row = table.AddRow();
            string desc = line.ItemDescription;
            if (!string.IsNullOrWhiteSpace(line.LotOrBatchNo)) desc += $" [Lot: {line.LotOrBatchNo}]";
            if (!string.IsNullOrWhiteSpace(line.RejectionReason)) desc += $" [Reject: {line.RejectionReason}]";
            SetCell(row, 0, line.ItemCode);
            SetCell(row, 1, desc);
            SetCell(row, 2, line.UnitName, ParagraphAlignment.Center);
            SetCell(row, 3, FormatQty(line.OrderedQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 4, FormatQty(line.ActualDeliveredQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 5, FormatQty(line.AcceptedQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 6, FormatQty(line.RejectedQtyX1000), ParagraphAlignment.Right);
            SetCell(row, 7, FormatMoney(line.UnitCostCents), ParagraphAlignment.Right);
            SetCell(row, 8, FormatMoney(line.AcceptedLineTotalCents), ParagraphAlignment.Right);
        }

        long totalAcceptedCents = rr.Lines.Sum(l => l.AcceptedLineTotalCents);
        AddTotalsSection(section, new[]
        {
            ("Total Accepted Value", FormatMoney(totalAcceptedCents), true)
        });
        AddNotesBlock(section, "Inspection / Receiving Notes", rr.Notes);
        if (!string.IsNullOrWhiteSpace(rr.OverdeliveryApprovalReason))
        {
            AddNotesBlock(section, "Overdelivery Exception Reason", rr.OverdeliveryApprovalReason);
        }
        AddSignatories(section, new[]
        {
            ("Received By", rr.ReceivedByName),
            ("Checked / Posted By", rr.PostedByUserId.HasValue ? rr.PostedByUserId.Value.ToString("D")[..8] : "________________________")
        });

        return RenderPdfBytes(document, $"RR-{rr.RrNumber}");
    }

    public static bool TryValidateCustomerQuotation(CustomerQuotation quotation, out string failureReason)
    {
        if (quotation is null)
        {
            failureReason = "Quotation is required.";
            return false;
        }

        if (quotation.Lines is null || quotation.Lines.Count == 0)
        {
            failureReason = "Quotation must contain at least one persisted line item before generating a PDF.";
            return false;
        }

        if (quotation.DiscountCents < 0 || quotation.SubtotalCents < 0 || quotation.TotalCents < 0)
        {
            failureReason = "Quotation totals and discount cannot be negative.";
            return false;
        }

        try
        {
            long computedSubtotal = 0;
            foreach (var line in quotation.Lines)
            {
                if (line.QuantityX1000 <= 0 || line.UnitPriceCents < 0 || line.DiscountCents < 0 || line.LineTotalCents < 0)
                {
                    failureReason = $"Quotation line '{line.ProductNameSnapshot}' has invalid non-positive quantity or negative pricing.";
                    return false;
                }

                long grossLineCents = checked(line.QuantityX1000 * line.UnitPriceCents + 500) / 1000L;
                if (line.DiscountCents > grossLineCents)
                {
                    failureReason = $"Quotation line '{line.ProductNameSnapshot}' discount ({line.DiscountCents}) exceeds gross line amount ({grossLineCents}).";
                    return false;
                }

                long expectedLineTotal = checked(grossLineCents - line.DiscountCents);
                if (line.LineTotalCents != expectedLineTotal)
                {
                    failureReason = $"Quotation line '{line.ProductNameSnapshot}' total ({line.LineTotalCents}) does not match expected net amount ({expectedLineTotal}).";
                    return false;
                }

                computedSubtotal = checked(computedSubtotal + line.LineTotalCents);
            }

            if (quotation.SubtotalCents != computedSubtotal)
            {
                failureReason = $"Quotation subtotal ({quotation.SubtotalCents}) does not match sum of line totals ({computedSubtotal}).";
                return false;
            }

            if (quotation.DiscountCents > computedSubtotal)
            {
                failureReason = $"Quotation discount ({quotation.DiscountCents}) exceeds quotation subtotal ({computedSubtotal}).";
                return false;
            }

            long expectedTotal = checked(computedSubtotal - quotation.DiscountCents);
            if (quotation.TotalCents != expectedTotal)
            {
                failureReason = $"Quotation total ({quotation.TotalCents}) does not match net total ({expectedTotal}).";
                return false;
            }
        }
        catch (OverflowException)
        {
            failureReason = "Quotation monetary calculations overflow 64-bit bounds.";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    public static byte[] BuildCustomerQuotationPdf(CustomerQuotation quotation, StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(quotation);
        if (!TryValidateCustomerQuotation(quotation, out string failureReason))
        {
            throw new InvalidOperationException(failureReason);
        }

        (Document document, Section section) = CreateA4Document($"Customer Quotation {quotation.QuoteNumber}", store);
        AddDocumentHeader(section, store, "PRICE QUOTATION / CANVASS", quotation.QuoteNumber, new[]
        {
            $"Status: {quotation.Status}",
            $"Created: {FormatManila(quotation.CreatedAt):yyyy-MM-dd HH:mm}",
            $"Valid Until: {quotation.ValidUntil?.ToString("yyyy-MM-dd") ?? "Open / Non-expiring"}",
            $"Prepared By: {quotation.CreatedByUserName}"
        });

        AddNoticeBanner(section, "PRICE ESTIMATE ONLY — NOT AN OFFICIAL RECEIPT / NOT A SALE / NO STOCK RESERVED", isWarning: true);

        AddMetadataGrid(section, new[]
        {
            ("Customer Name", quotation.CustomerName),
            ("Contact Details", quotation.CustomerContact ?? "N/A")
        });

        Table table = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
        {
            ("#", 0.9, ParagraphAlignment.Center),
            ("Product / Item", 6.2, ParagraphAlignment.Left),
            ("Barcode", 2.4, ParagraphAlignment.Left),
            ("UOM", 1.4, ParagraphAlignment.Center),
            ("Qty", 1.8, ParagraphAlignment.Right),
            ("Unit Price", 2.0, ParagraphAlignment.Right),
            ("Discount", 1.7, ParagraphAlignment.Right),
            ("Line Total", 2.0, ParagraphAlignment.Right)
        });

        int idx = 1;
        foreach (var line in quotation.Lines)
        {
            Row row = table.AddRow();
            SetCell(row, 0, idx++.ToString(CultureInfo.InvariantCulture), ParagraphAlignment.Center);
            SetCell(row, 1, line.ProductNameSnapshot);
            SetCell(row, 2, line.BarcodeSnapshot ?? "-");
            SetCell(row, 3, line.UnitNameSnapshot, ParagraphAlignment.Center);
            SetCell(row, 4, FormatQty(line.QuantityX1000), ParagraphAlignment.Right);
            SetCell(row, 5, FormatMoney(line.UnitPriceCents), ParagraphAlignment.Right);
            SetCell(row, 6, FormatMoney(line.DiscountCents), ParagraphAlignment.Right);
            SetCell(row, 7, FormatMoney(line.LineTotalCents), ParagraphAlignment.Right);
        }

        AddTotalsSection(section, new[]
        {
            ("Subtotal", FormatMoney(quotation.SubtotalCents), false),
            ("Quote Discount", FormatMoney(quotation.DiscountCents), false),
            ("Quoted Total", FormatMoney(quotation.TotalCents), true)
        });
        AddNotesBlock(section, "Notes & Validity Conditions", quotation.Notes);

        return RenderPdfBytes(document, $"QT-{quotation.QuoteNumber}");
    }

    public static byte[] BuildCustomerOrderPdf(CustomerOrder order, StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Lines.Count == 0 || order.TotalCents < 0 || order.Lines.Sum(l => l.LineTotalCents) != order.TotalCents)
        {
            throw new InvalidOperationException("Customer order lines or totals failed integrity validation.");
        }

        (Document document, Section section) = CreateA4Document($"Customer Order {order.OrderNumber}", store);
        AddDocumentHeader(section, store, "CUSTOMER ORDER / BOOKING SLIP", order.OrderNumber, new[]
        {
            $"Status: {order.Status}",
            $"Created: {FormatManila(order.CreatedAt):yyyy-MM-dd HH:mm}",
            $"Promised Date: {order.PromisedDate?.ToString("yyyy-MM-dd") ?? "TBD"}",
            $"Prepared By: {order.CreatedByUserName}"
        });

        AddNoticeBanner(section, "COMMERCIAL CUSTOMER ORDER SLIP — NOT AN OFFICIAL BIR RECEIPT", isWarning: false);

        AddMetadataGrid(section, new[]
        {
            ("Customer Name", order.CustomerName),
            ("Phone / Contact", order.CustomerPhone ?? "N/A"),
            ("Linked Sale ID", order.SaleId?.ToString("D") ?? "Pending POS Fulfillment"),
            ("Fulfilled At", order.FulfilledAt.HasValue ? $"{FormatManila(order.FulfilledAt.Value):yyyy-MM-dd HH:mm}" : "Not Yet Fulfilled")
        });

        Table table = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
        {
            ("#", 0.9, ParagraphAlignment.Center),
            ("Product / Item", 6.7, ParagraphAlignment.Left),
            ("Barcode", 2.7, ParagraphAlignment.Left),
            ("UOM", 1.5, ParagraphAlignment.Center),
            ("Qty", 2.0, ParagraphAlignment.Right),
            ("Unit Price", 2.3, ParagraphAlignment.Right),
            ("Line Total", 2.3, ParagraphAlignment.Right)
        });

        int idx = 1;
        foreach (var line in order.Lines)
        {
            Row row = table.AddRow();
            SetCell(row, 0, idx++.ToString(CultureInfo.InvariantCulture), ParagraphAlignment.Center);
            SetCell(row, 1, line.ProductNameSnapshot);
            SetCell(row, 2, line.BarcodeSnapshot ?? "-");
            SetCell(row, 3, line.UnitNameSnapshot, ParagraphAlignment.Center);
            SetCell(row, 4, FormatQty(line.QuantityX1000), ParagraphAlignment.Right);
            SetCell(row, 5, FormatMoney(line.UnitPriceCents), ParagraphAlignment.Right);
            SetCell(row, 6, FormatMoney(line.LineTotalCents), ParagraphAlignment.Right);
        }

        AddTotalsSection(section, new[]
        {
            ("Total Order Amount", FormatMoney(order.TotalCents), true)
        });
        AddNotesBlock(section, "Order Remarks", order.Notes);
        AddSignatories(section, new[]
        {
            ("Encoded By", order.CreatedByUserName),
            ("Customer / Authorized Rep", "________________________")
        });

        return RenderPdfBytes(document, $"ORD-{order.OrderNumber}");
    }

    public static byte[] BuildSupplierPaymentVoucherPdf(
        SupplierPaymentModel payment,
        SupplierInvoiceModel? invoice,
        StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(payment);
        bool isSettled = string.Equals(payment.Status, SupplierPaymentStatuses.Settled, StringComparison.Ordinal);

        (Document document, Section section) = CreateA4Document($"AP Disbursement Voucher {payment.PaymentNumber}", store);
        AddDocumentHeader(section, store, "CHECK / DISBURSEMENT VOUCHER", payment.PaymentNumber, new[]
        {
            $"Status: {(isSettled ? "Settled" : payment.Status)}",
            $"AP Voucher: {payment.ApVoucherNumber}",
            $"Invoice No: {payment.SupplierInvoiceNo}",
            $"Scheduled Date: {payment.PaymentDate:yyyy-MM-dd}"
        });

        AddNoticeBanner(
            section,
            isSettled
                ? "SETTLED — OPERATOR-CONFIRMED DISBURSEMENT (DEDUCTED FROM SUPPLIER AP BALANCE)"
                : "DRAFT / PENDING APPROVAL — NOT YET DISBURSED (AP BALANCE NOT YET DEDUCTED)",
            isWarning: !isSettled);

        AddMetadataGrid(section, new[]
        {
            ("Payee / Supplier", payment.SupplierName),
            ("Disbursement Amount", FormatMoney(payment.AmountCents)),
            ("Payment Method", payment.PaymentMethod),
            ("Disbursement Source", payment.DisbursementSource),
            ("Reference No", payment.ReferenceNo),
            ("Check / Bank", $"{payment.CheckNo ?? "N/A"} / {payment.BankName ?? "N/A"}"),
            ("Actual Disbursement Date", isSettled ? (payment.ActualDisbursementDate?.ToString("yyyy-MM-dd") ?? "Recorded") : "Not yet disbursed"),
            ("Settlement Confirmed At", isSettled && payment.ApprovedAt.HasValue ? $"{FormatManila(payment.ApprovedAt.Value):yyyy-MM-dd HH:mm} (UTC+08)" : "Pending confirmation")
        });

        if (invoice is not null)
        {
            Table invTable = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
            {
                ("APV Number", 3.6, ParagraphAlignment.Left),
                ("Invoice Total", 3.7, ParagraphAlignment.Right),
                ("Credits / Paid", 3.7, ParagraphAlignment.Right),
                ("This Voucher", 3.7, ParagraphAlignment.Right),
                ("Open Balance", 3.7, ParagraphAlignment.Right)
            });
            Row row = invTable.AddRow();
            SetCell(row, 0, invoice.ApVoucherNumber);
            SetCell(row, 1, FormatMoney(invoice.TotalInvoiceCents), ParagraphAlignment.Right);
            SetCell(row, 2, FormatMoney(invoice.PaidAmountCents + invoice.CreditMemoAppliedCents), ParagraphAlignment.Right);
            SetCell(row, 3, FormatMoney(payment.AmountCents), ParagraphAlignment.Right);
            SetCell(row, 4, FormatMoney(invoice.RemainingBalanceCents), ParagraphAlignment.Right);
        }

        AddTotalsSection(section, new[]
        {
            (isSettled ? "Net Disbursed Amount" : "Pending Voucher Request Amount", FormatMoney(payment.AmountCents), true)
        });
        AddNotesBlock(section, "Particulars / Remarks", payment.Remarks);
        AddSignatories(section, new[]
        {
            ("Prepared By", payment.RecordedByUserId.ToString("D")[..8]),
            ("Approved By", payment.ApprovedByUserId?.ToString("D")[..8] ?? "Pending Approval"),
            ("Received By Payee", "________________________")
        });

        return RenderPdfBytes(document, $"DV-{payment.PaymentNumber}");
    }

    public static byte[] BuildCollectionAcknowledgmentPdf(CollectionPaymentAcknowledgment ack, StoreSettings? store)
    {
        ArgumentNullException.ThrowIfNull(ack);
        string shortRef = ack.CollectionId.ToString("D")[..8].ToUpperInvariant();
        (Document document, Section section) = CreateA4Document($"AR Collection Acknowledgment {shortRef}", store);
        AddDocumentHeader(section, store, "AR COLLECTION ACKNOWLEDGMENT", $"ACK-{shortRef}", new[]
        {
            "Type: Utang / AR Payment Receipt",
            $"Collected At: {FormatManila(ack.CollectedAt):yyyy-MM-dd HH:mm:ss}",
            $"Method: {ack.Method}",
            $"Collected By: {ack.CollectedBy}"
        });

        AddNoticeBanner(section, "OPERATIONAL AR PAYMENT ACKNOWLEDGMENT — NOT AN OFFICIAL BIR RECEIPT OR TAX INVOICE", isWarning: false);

        AddMetadataGrid(section, new[]
        {
            ("Customer Name", ack.CustomerName),
            ("Collection ID", ack.CollectionId.ToString("D")),
            ("Reference / Note", string.IsNullOrWhiteSpace(ack.Note) ? "None" : ack.Note!)
        });

        Table rollTable = CreateStyledTable(section, new (string Label, double WidthCm, ParagraphAlignment Align)[]
        {
            ("Previous AR Balance", 6.1, ParagraphAlignment.Right),
            ("Amount Paid (Credited)", 6.1, ParagraphAlignment.Right),
            ("New Remaining Balance", 6.2, ParagraphAlignment.Right)
        });
        Row row = rollTable.AddRow();
        SetCell(row, 0, FormatMoney(ack.PreviousBalanceCents), ParagraphAlignment.Right);
        SetCell(row, 1, FormatMoney(ack.AmountPaidCents), ParagraphAlignment.Right);
        SetCell(row, 2, FormatMoney(ack.NewBalanceCents), ParagraphAlignment.Right);
        row.Format.Font.Bold = true;

        AddTotalsSection(section, new[]
        {
            ("Amount Received", FormatMoney(ack.AmountPaidCents), true),
            ("Ending Customer Balance", FormatMoney(ack.NewBalanceCents), false)
        });
        AddSignatories(section, new[]
        {
            ("Collected By", ack.CollectedBy),
            ("Customer / Payor", "________________________")
        });

        return RenderPdfBytes(document, $"AR-ACK-{shortRef}");
    }

    private static (Document Document, Section Section) CreateA4Document(string title, StoreSettings? store)
    {
        Document document = new();
        document.Info.Title = title;
        document.Info.Author = store?.StoreName ?? "Salryn";

        Style normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;
        normal.Font.Color = Colors.Black;

        Section section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.3);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.3);

        Paragraph footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Colors.DimGray;
        footer.AddText("INTERNAL BUSINESS DOCUMENT — NOT AN OFFICIAL BIR RECEIPT OR TAX INVOICE   |   Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        return (document, section);
    }

    private static void AddDocumentHeader(
        Section section,
        StoreSettings? store,
        string docTitle,
        string docNumber,
        IReadOnlyList<string> rightLines)
    {
        Paragraph storeTitle = section.AddParagraph(store?.StoreName is { Length: > 0 } s ? s.ToUpperInvariant() : "SALRYN STORE");
        storeTitle.Format.Font.Size = 14;
        storeTitle.Format.Font.Bold = true;
        storeTitle.Format.Alignment = ParagraphAlignment.Center;

        if (!string.IsNullOrWhiteSpace(store?.BusinessName))
        {
            Paragraph biz = section.AddParagraph(store.BusinessName);
            biz.Format.Font.Size = 8.5;
            biz.Format.Alignment = ParagraphAlignment.Center;
        }
        if (!string.IsNullOrWhiteSpace(store?.Address))
        {
            Paragraph addr = section.AddParagraph(store.Address);
            addr.Format.Font.Size = 8;
            addr.Format.Font.Color = Colors.DimGray;
            addr.Format.Alignment = ParagraphAlignment.Center;
        }
        List<string> contacts = new();
        if (!string.IsNullOrWhiteSpace(store?.ContactNumber)) contacts.Add($"Tel: {store.ContactNumber}");
        if (!string.IsNullOrWhiteSpace(store?.Tin)) contacts.Add($"TIN: {store.Tin}");
        if (contacts.Count > 0)
        {
            Paragraph contactLine = section.AddParagraph(string.Join("   |   ", contacts));
            contactLine.Format.Font.Size = 8;
            contactLine.Format.Font.Color = Colors.DimGray;
            contactLine.Format.Alignment = ParagraphAlignment.Center;
        }

        Paragraph divider = section.AddParagraph();
        divider.Format.Borders.Bottom.Width = 0.75;
        divider.Format.SpaceAfter = Unit.FromCentimeter(0.25);

        Table banner = section.AddTable();
        banner.Borders.Width = 0;
        banner.AddColumn(Unit.FromCentimeter(10.4));
        banner.AddColumn(Unit.FromCentimeter(8.0));
        Row row = banner.AddRow();

        Paragraph left = row.Cells[0].AddParagraph(docTitle);
        left.Format.Font.Size = 12;
        left.Format.Font.Bold = true;
        Paragraph num = row.Cells[0].AddParagraph(docNumber);
        num.Format.Font.Size = 11;
        num.Format.Font.Bold = true;
        num.Format.Font.Color = Color.FromRgb(30, 58, 138);

        foreach (string line in rightLines)
        {
            Paragraph r = row.Cells[1].AddParagraph(line);
            r.Format.Font.Size = 8.5;
            r.Format.Alignment = ParagraphAlignment.Right;
        }

        banner.Format.SpaceAfter = Unit.FromCentimeter(0.25);
    }

    private static void AddNoticeBanner(Section section, string text, bool isWarning)
    {
        Table box = section.AddTable();
        box.Borders.Width = 0.5;
        box.Borders.Color = isWarning ? Color.FromRgb(217, 119, 6) : Color.FromRgb(59, 130, 246);
        box.AddColumn(Unit.FromCentimeter(PrintableWidthCm));
        Row r = box.AddRow();
        r.Shading.Color = isWarning ? Color.FromRgb(254, 243, 199) : Color.FromRgb(239, 246, 255);
        r.TopPadding = Unit.FromPoint(4);
        r.BottomPadding = Unit.FromPoint(4);
        Paragraph p = r.Cells[0].AddParagraph(text);
        p.Format.LeftIndent = Unit.FromPoint(6);
        p.Format.RightIndent = Unit.FromPoint(6);
        p.Format.Font.Size = 8.5;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = isWarning ? Color.FromRgb(146, 64, 14) : Color.FromRgb(30, 64, 175);
        box.Format.SpaceAfter = Unit.FromCentimeter(0.25);
    }

    private static void AddMetadataGrid(Section section, IReadOnlyList<(string Key, string Value)> pairs)
    {
        Table table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = Color.FromRgb(203, 213, 225);
        table.AddColumn(Unit.FromCentimeter(3.8));
        table.AddColumn(Unit.FromCentimeter(5.4));
        table.AddColumn(Unit.FromCentimeter(3.8));
        table.AddColumn(Unit.FromCentimeter(5.4));

        for (int i = 0; i < pairs.Count; i += 2)
        {
            Row row = table.AddRow();
            row.TopPadding = Unit.FromPoint(3);
            row.BottomPadding = Unit.FromPoint(3);
            row.Cells[0].Shading.Color = Color.FromRgb(248, 250, 252);
            row.Cells[0].AddParagraph(pairs[i].Key).Format.Font.Bold = true;
            row.Cells[1].AddParagraph(pairs[i].Value ?? string.Empty);

            if (i + 1 < pairs.Count)
            {
                row.Cells[2].Shading.Color = Color.FromRgb(248, 250, 252);
                row.Cells[2].AddParagraph(pairs[i + 1].Key).Format.Font.Bold = true;
                row.Cells[3].AddParagraph(pairs[i + 1].Value ?? string.Empty);
            }
            else
            {
                row.Cells[2].Shading.Color = Color.FromRgb(248, 250, 252);
                row.Cells[2].AddParagraph(string.Empty);
                row.Cells[3].AddParagraph(string.Empty);
            }
        }

        Paragraph spacer = section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromCentimeter(0.2);
    }

    private static Table CreateStyledTable(
        Section section,
        IReadOnlyList<(string Label, double WidthCm, ParagraphAlignment Align)> columns)
    {
        Table table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = Color.FromRgb(203, 213, 225);
        table.Format.Font.Size = 8;

        foreach (var col in columns)
        {
            table.AddColumn(Unit.FromCentimeter(col.WidthCm));
        }

        Row header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = Color.FromRgb(241, 245, 249);
        header.TopPadding = Unit.FromPoint(4);
        header.BottomPadding = Unit.FromPoint(4);

        for (int i = 0; i < columns.Count; i++)
        {
            Paragraph p = header.Cells[i].AddParagraph(columns[i].Label);
            p.Format.Alignment = columns[i].Align;
        }

        return table;
    }

    private static void SetCell(Row row, int index, string text, ParagraphAlignment align = ParagraphAlignment.Left)
    {
        row.TopPadding = Unit.FromPoint(3);
        row.BottomPadding = Unit.FromPoint(3);
        Paragraph p = row.Cells[index].AddParagraph(text ?? string.Empty);
        p.Format.Alignment = align;
    }

    private static void AddTotalsSection(
        Section section,
        IReadOnlyList<(string Label, string Value, bool Emphasize)> items)
    {
        Paragraph spacer = section.AddParagraph();
        spacer.Format.SpaceBefore = Unit.FromCentimeter(0.15);

        Table totals = section.AddTable();
        totals.Borders.Width = 0.25;
        totals.Borders.Color = Color.FromRgb(203, 213, 225);
        totals.AddColumn(Unit.FromCentimeter(11.4));
        totals.AddColumn(Unit.FromCentimeter(4.0));
        totals.AddColumn(Unit.FromCentimeter(3.0));

        foreach (var (label, value, emphasize) in items)
        {
            Row row = totals.AddRow();
            row.Borders.Left.Visible = false;
            row.Cells[0].Borders.Visible = false;
            row.Cells[1].Shading.Color = emphasize ? Color.FromRgb(241, 245, 249) : Colors.White;
            row.Cells[2].Shading.Color = emphasize ? Color.FromRgb(241, 245, 249) : Colors.White;
            Paragraph lp = row.Cells[1].AddParagraph(label);
            Paragraph vp = row.Cells[2].AddParagraph(value);
            lp.Format.Font.Bold = emphasize;
            vp.Format.Font.Bold = emphasize;
            vp.Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void AddNotesBlock(Section section, string heading, string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return;
        Paragraph h = section.AddParagraph(heading);
        h.Format.SpaceBefore = Unit.FromCentimeter(0.25);
        h.Format.Font.Bold = true;
        h.Format.Font.Size = 8.5;
        Paragraph body = section.AddParagraph(notes);
        body.Format.Font.Size = 8.5;
        body.Format.Font.Color = Colors.DarkSlateGray;
    }

    private static void AddSignatories(Section section, IReadOnlyList<(string Role, string Name)> signers)
    {
        Paragraph spacer = section.AddParagraph();
        spacer.Format.SpaceBefore = Unit.FromCentimeter(0.45);

        Table sig = section.AddTable();
        sig.Borders.Width = 0;
        double colWidth = PrintableWidthCm / Math.Max(1, signers.Count);
        foreach (var _ in signers)
        {
            sig.AddColumn(Unit.FromCentimeter(colWidth));
        }

        Row nameRow = sig.AddRow();
        nameRow.KeepWith = 1;
        Row roleRow = sig.AddRow();
        for (int i = 0; i < signers.Count; i++)
        {
            Paragraph np = nameRow.Cells[i].AddParagraph(signers[i].Name);
            np.Format.Font.Bold = true;
            np.Format.Font.Size = 8.5;
            np.Format.Alignment = ParagraphAlignment.Center;

            Paragraph rp = roleRow.Cells[i].AddParagraph(signers[i].Role);
            rp.Format.Font.Size = 7.5;
            rp.Format.Font.Color = Colors.DimGray;
            rp.Format.Alignment = ParagraphAlignment.Center;
        }
    }

    private static byte[] RenderPdfBytes(Document document, string subject)
    {
        document.Info.Subject = subject;
        PdfDocumentRenderer renderer = new()
        {
            Document = document
        };
        renderer.RenderDocument();
        renderer.PdfDocument.Info.Creator = "Salryn Local Document PDF";
        using MemoryStream stream = new();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static DateTimeOffset FormatManila(DateTimeOffset utcOrOffset) => utcOrOffset.ToOffset(ManilaOffset);

    private static string FormatQty(long qtyX1000) =>
        (qtyX1000 / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatMoney(long cents) =>
        "PHP " + (cents / 100m).ToString("N2", CultureInfo.InvariantCulture);
}
