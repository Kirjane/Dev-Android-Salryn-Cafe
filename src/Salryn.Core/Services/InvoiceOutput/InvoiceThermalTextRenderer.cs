using System.Globalization;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services.InvoiceOutput;

/// <summary>Deterministic fixed-width thermal/plain-text invoice projection.</summary>
public sealed class InvoiceThermalTextRenderer
{
    public Result<InvoiceOutputArtifact> Render(InvoiceRenderModel model)
    {
        if (model is null)
        {
            return Result<InvoiceOutputArtifact>.Fail(Error.Validation(
                "InvoiceThermal.ModelRequired",
                "Invoice render model is required."));
        }

        int w = InvoiceOutputConstants.ThermalWidth;
        StringBuilder sb = new();

        if (model.RequiresInternalWatermark)
        {
            Line(sb, Center(model.InternalWatermark, w));
            Line(sb, Repeat('-', w));
        }

        Line(sb, Center(model.DocumentTitle.ToUpperInvariant(), w));
        Line(sb, Center("INVOICE", w));
        Line(sb, Repeat('=', w));
        Line(sb, Wrap(model.SellerRegisteredName, w));
        if (!string.IsNullOrWhiteSpace(model.SellerBusinessName))
        {
            Line(sb, Wrap(model.SellerBusinessName!, w));
        }

        Line(sb, Wrap(model.RegistrationPhrase + ": " + model.SellerTin + "/" + model.SellerBranchCode, w));
        Line(sb, Wrap(model.SellerRegisteredAddress, w));
        Line(sb, "No: " + model.OfficialDocumentNumber);
        Line(sb, "DT: " + model.TransactionDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Line(sb, "Cashier: " + model.CashierIdentifier);
        Line(sb, Wrap(
            "Buyer: " + (string.IsNullOrWhiteSpace(model.BuyerNameSpace) ? "____________________" : model.BuyerNameSpace!),
            w));
        Line(sb, Wrap(
            "Buyer TIN: " + (string.IsNullOrWhiteSpace(model.BuyerTinSpace) ? "________________" : model.BuyerTinSpace!),
            w));
        if (!string.IsNullOrWhiteSpace(model.BuyerAddressSpace))
        {
            Line(sb, Wrap("Buyer Addr: " + model.BuyerAddressSpace, w));
        }

        if (!string.IsNullOrWhiteSpace(model.StatutoryDiscountCategory))
        {
            Line(sb, Wrap("Stat disc: " + model.StatutoryDiscountCategory, w));
        }

        Line(sb, Repeat('-', w));

        foreach (InvoiceRenderLine line in model.Lines)
        {
            Line(sb, Wrap(line.Description, w));
            string left = FormatQty(line.QuantityX1000) + " x " + FormatMoney(line.UnitPriceCents);
            string right = FormatMoney(line.LineTotalCents);
            Line(sb, PadRow(left, right, w));
        }

        Line(sb, Repeat('-', w));
        Line(sb, PadRow("SUBTOTAL", FormatMoney(model.SubtotalCents), w));
        Line(sb, PadRow("DISCOUNT", FormatMoney(model.DiscountCents), w));

        foreach (InvoiceRenderTaxBucket bucket in model.TaxBuckets)
        {
            if (bucket.SalesAmountCents == 0 && bucket.TaxAmountCents == 0)
            {
                continue;
            }

            long amount = bucket.TaxAmountCents > 0 ? bucket.TaxAmountCents : bucket.SalesAmountCents;
            Line(sb, PadRow(ShortBucket(bucket.Kind), FormatMoney(amount), w));
        }

        Line(sb, PadRow("TOTAL DUE", FormatMoney(model.TotalCents), w));
        Line(sb, PadRow("PAID", FormatMoney(model.AmountPaidCents), w));
        Line(sb, PadRow("CHANGE", FormatMoney(model.ChangeCents), w));

        if (!string.IsNullOrWhiteSpace(model.NonVatDisclaimer))
        {
            Line(sb, Repeat('-', w));
            Line(sb, Wrap(model.NonVatDisclaimer!, w));
        }

        if (model.RequiresInternalWatermark)
        {
            Line(sb, Repeat('-', w));
            Line(sb, Center(model.InternalWatermark, w));
        }

        Line(sb, Repeat('=', w));
        Line(sb, "Variant: " + model.Variant);
        Line(sb, "Snap: " + Trunc(model.SnapshotPayloadHash, w));
        Line(sb, Wrap(InvoiceOutputConstants.NoComplianceClaimNote, w));

        string text = sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.EndsWith('\n'))
        {
            text += "\n";
        }

        // Normalize to LF-only for byte determinism across platforms.
        string hash = OfficialInvoiceSnapshotParser.ComputeSha256Hex(text);
        return Result<InvoiceOutputArtifact>.Success(new InvoiceOutputArtifact(
            InvoiceOutputFormat.ThermalText,
            "text/plain; charset=utf-8",
            SanitizeFileName(model.OfficialDocumentNumber) + ".txt",
            text,
            hash));
    }

    private static void Line(StringBuilder sb, string value) => sb.Append(value).Append('\n');

    private static string Center(string value, int width)
    {
        if (value.Length >= width)
        {
            return value[..width];
        }

        int pad = (width - value.Length) / 2;
        return new string(' ', pad) + value;
    }

    private static string PadRow(string left, string right, int width)
    {
        if (left.Length + right.Length + 1 > width)
        {
            return left[..Math.Max(0, width - right.Length - 1)] + " " + right;
        }

        return left + new string(' ', width - left.Length - right.Length) + right;
    }

    private static string Wrap(string value, int width)
    {
        if (value.Length <= width)
        {
            return value;
        }

        StringBuilder sb = new();
        int i = 0;
        while (i < value.Length)
        {
            int take = Math.Min(width, value.Length - i);
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(value, i, take);
            i += take;
        }

        return sb.ToString();
    }

    private static string Repeat(char c, int n) => new(c, n);

    private static string FormatMoney(long cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatQty(long quantityX1000) =>
        (quantityX1000 / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    private static string ShortBucket(string kind) => kind switch
    {
        "VatableSales" => "VATABLE",
        "VatAmount" => "VAT",
        "VatExemptSales" => "VAT-EXEMPT",
        "ZeroRatedSales" => "ZERO-RATED",
        "SsptSales" => "SSPT",
        "NonVatExemptSales" => "NV-EXEMPT",
        "NonVatSales" => "NON-VAT",
        _ => kind.ToUpperInvariant()
    };

    private static string Trunc(string value, int width) =>
        value.Length <= width ? value : value[..width];

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
