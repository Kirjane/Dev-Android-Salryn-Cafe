using System.Globalization;
using System.Net;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services.InvoiceOutput;

/// <summary>Deterministic self-contained HTML invoice projection. No scripts, no external CSS.</summary>
public sealed class InvoiceHtmlRenderer
{
    public Result<InvoiceOutputArtifact> Render(InvoiceRenderModel model)
    {
        if (model is null)
        {
            return Result<InvoiceOutputArtifact>.Fail(Error.Validation(
                "InvoiceHtml.ModelRequired",
                "Invoice render model is required."));
        }

        StringBuilder sb = new();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<title>").Append(E(model.DocumentTitle)).Append(" ").Append(E(model.OfficialDocumentNumber)).Append("</title>");
        sb.Append("<style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;font-size:12px;color:#111;margin:16px;}");
        sb.Append("h1{font-size:18px;margin:0 0 8px 0;text-align:center;}");
        sb.Append(".wm{color:#b00020;font-weight:700;text-align:center;border:2px solid #b00020;padding:6px;margin:8px 0;}");
        sb.Append("table{width:100%;border-collapse:collapse;margin-top:8px;}");
        sb.Append("th,td{border-bottom:1px solid #ccc;padding:4px;text-align:left;vertical-align:top;}");
        sb.Append("th.num,td.num{text-align:right;white-space:nowrap;}");
        sb.Append(".meta{margin:2px 0;}.tot{font-weight:700;}.disc{font-size:11px;color:#333;margin-top:10px;}");
        sb.Append("</style></head><body>");

        if (model.RequiresInternalWatermark)
        {
            sb.Append("<div class=\"wm\">").Append(E(model.InternalWatermark)).Append("</div>");
        }

        sb.Append("<h1>").Append(E(model.DocumentTitle.ToUpperInvariant())).Append("</h1>");
        sb.Append("<div class=\"meta\"><strong>INVOICE</strong></div>");
        sb.Append("<div class=\"meta\">").Append(E(model.SellerRegisteredName)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.SellerBusinessName))
        {
            sb.Append("<div class=\"meta\">").Append(E(model.SellerBusinessName)).Append("</div>");
        }

        sb.Append("<div class=\"meta\">").Append(E(model.RegistrationPhrase)).Append(": ")
            .Append(E(model.SellerTin)).Append(" / ").Append(E(model.SellerBranchCode)).Append("</div>");
        sb.Append("<div class=\"meta\">").Append(E(model.SellerRegisteredAddress)).Append("</div>");
        sb.Append("<div class=\"meta\">Invoice No: <strong>").Append(E(model.OfficialDocumentNumber)).Append("</strong></div>");
        sb.Append("<div class=\"meta\">Date/Time: ")
            .Append(E(model.TransactionDateTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)))
            .Append("</div>");

        if (!string.IsNullOrWhiteSpace(model.MachineSerialNumber))
        {
            sb.Append("<div class=\"meta\">Machine Serial: ").Append(E(model.MachineSerialNumber)).Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(model.MachineIdentificationNumber))
        {
            sb.Append("<div class=\"meta\">MIN: ").Append(E(model.MachineIdentificationNumber)).Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(model.AccreditationNumber))
        {
            sb.Append("<div class=\"meta\">Accreditation No: ").Append(E(model.AccreditationNumber)).Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(model.PermitToUseNumber))
        {
            sb.Append("<div class=\"meta\">PTU No: ").Append(E(model.PermitToUseNumber)).Append("</div>");
        }

        sb.Append("<div class=\"meta\">Cashier: ").Append(E(model.CashierIdentifier)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(model.TerminalIdentifier))
        {
            sb.Append("<div class=\"meta\">Terminal: ").Append(E(model.TerminalIdentifier)).Append("</div>");
        }

        sb.Append("<div class=\"meta\">Buyer Name: ____________________</div>");
        sb.Append("<div class=\"meta\">Buyer TIN: ____________________</div>");
        sb.Append("<div class=\"meta\">Buyer Address: ____________________</div>");

        sb.Append("<table><thead><tr><th>Description</th><th class=\"num\">Qty</th><th class=\"num\">Unit</th><th class=\"num\">Amount</th></tr></thead><tbody>");
        foreach (InvoiceRenderLine line in model.Lines)
        {
            sb.Append("<tr><td>").Append(E(line.Description)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(E(FormatQty(line.QuantityX1000))).Append("</td>");
            sb.Append("<td class=\"num\">").Append(E(FormatMoney(line.UnitPriceCents))).Append("</td>");
            sb.Append("<td class=\"num\">").Append(E(FormatMoney(line.LineTotalCents))).Append("</td></tr>");
        }

        sb.Append("</tbody></table>");

        sb.Append("<div class=\"meta tot\">Subtotal: ").Append(E(FormatMoney(model.SubtotalCents))).Append("</div>");
        sb.Append("<div class=\"meta\">Discount: ").Append(E(FormatMoney(model.DiscountCents))).Append("</div>");

        foreach (InvoiceRenderTaxBucket bucket in model.TaxBuckets)
        {
            if (bucket.SalesAmountCents == 0 && bucket.TaxAmountCents == 0)
            {
                continue;
            }

            if (bucket.TaxAmountCents > 0)
            {
                sb.Append("<div class=\"meta\">").Append(E(HumanBucket(bucket.Kind))).Append(": ")
                    .Append(E(FormatMoney(bucket.TaxAmountCents))).Append("</div>");
            }
            else
            {
                sb.Append("<div class=\"meta\">").Append(E(HumanBucket(bucket.Kind))).Append(": ")
                    .Append(E(FormatMoney(bucket.SalesAmountCents))).Append("</div>");
            }
        }

        sb.Append("<div class=\"meta tot\">TOTAL AMOUNT DUE: ").Append(E(FormatMoney(model.TotalCents))).Append("</div>");
        sb.Append("<div class=\"meta\">Amount Paid: ").Append(E(FormatMoney(model.AmountPaidCents))).Append("</div>");
        sb.Append("<div class=\"meta\">Change: ").Append(E(FormatMoney(model.ChangeCents))).Append("</div>");

        foreach (InvoiceRenderPayment payment in model.Payments)
        {
            sb.Append("<div class=\"meta\">Payment (").Append(E(payment.Method)).Append("): ")
                .Append(E(FormatMoney(payment.AmountCents))).Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(model.NonVatDisclaimer))
        {
            sb.Append("<div class=\"meta tot\">").Append(E(model.NonVatDisclaimer)).Append("</div>");
        }

        if (model.RequiresInternalWatermark)
        {
            sb.Append("<div class=\"wm\">").Append(E(model.InternalWatermark)).Append("</div>");
        }

        sb.Append("<div class=\"disc\">Variant: ").Append(E(model.Variant.ToString())).Append("</div>");
        sb.Append("<div class=\"disc\">Snapshot hash: ").Append(E(model.SnapshotPayloadHash)).Append("</div>");
        foreach (string note in model.BoundaryNotes)
        {
            sb.Append("<div class=\"disc\">").Append(E(note)).Append("</div>");
        }

        if (model.MissingOptionalFields.Count > 0)
        {
            sb.Append("<div class=\"disc\">Missing optional fields: ")
                .Append(E(string.Join(", ", model.MissingOptionalFields))).Append("</div>");
        }

        sb.Append("</body></html>");

        string html = sb.ToString();
        string hash = OfficialInvoiceSnapshotParser.ComputeSha256Hex(html);
        return Result<InvoiceOutputArtifact>.Success(new InvoiceOutputArtifact(
            InvoiceOutputFormat.Html,
            "text/html; charset=utf-8",
            SanitizeFileName(model.OfficialDocumentNumber) + ".html",
            html,
            hash));
    }

    private static string HumanBucket(string kind) => kind switch
    {
        "VatableSales" => "VATable Sales (net of VAT)",
        "VatAmount" => "VAT Amount",
        "VatExemptSales" => "VAT-Exempt Sales",
        "ZeroRatedSales" => "Zero-Rated Sales",
        "SsptSales" => "Sales Subject to Percentage Tax (SSPT)",
        "NonVatExemptSales" => "Non-VAT Exempt Sales",
        "NonVatSales" => "Non-VAT Sales",
        _ => kind
    };

    private static string FormatMoney(long cents)
    {
        decimal pesos = cents / 100m;
        return "PHP " + pesos.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatQty(long quantityX1000)
    {
        decimal qty = quantityX1000 / 1000m;
        return qty.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new();
        foreach (char c in value)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        return string.IsNullOrWhiteSpace(sb.ToString()) ? "invoice" : sb.ToString();
    }
}
