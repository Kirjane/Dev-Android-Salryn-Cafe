using Salryn.Core.Official;

namespace Salryn.Core.Services;

/// <summary>
/// RMO-BIR-M9 internal report renderers.
/// JSON / plain text / internal HTML preview only. No third-party report engine, PDF, printer, or legal output.
/// </summary>
public static class OfficialInternalReportRenderer
{
    /// <summary>Same text as OfficialSafetyConstants.InternalWatermark (kept literal for source gates).</summary>
    public const string WatermarkLiteral =
        "INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE";

    public static string Watermark => OfficialSafetyConstants.InternalWatermark;

    public static string EnsureWatermark(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Watermark;
        }

        return text.Contains(Watermark, StringComparison.Ordinal)
            ? text
            : Watermark + Environment.NewLine + text + Environment.NewLine + Watermark;
    }

    public static string ToInternalHtmlPreview(string plainText)
    {
        string escaped = System.Net.WebUtility.HtmlEncode(plainText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);
        return "<div class=\"salryn-internal-report-preview\">" +
               "<p><strong>" + System.Net.WebUtility.HtmlEncode(Watermark) + "</strong></p>" +
               "<pre>" + escaped + "</pre>" +
               "<p><strong>" + System.Net.WebUtility.HtmlEncode(Watermark) + "</strong></p>" +
               "</div>";
    }
}

public static class OfficialInternalReceiptPreviewRenderer
{
    public static OfficialReceiptPreviewContract Render(OfficialDryRunPostingPlan plan)
    {
        string number = string.IsNullOrWhiteSpace(plan.Numbering.FormattedPreviewNumber)
            ? "(preview blocked)"
            : plan.Numbering.FormattedPreviewNumber!;

        List<string> header = new()
        {
            OfficialSafetyConstants.InternalWatermark,
            "Document kind: " + plan.DocumentKind,
            "Preview official number: " + number,
            "Operational number hint: " + (plan.OperationalPosNumberHint ?? "(none)"),
            "Dual numbering: operational POS number remains separate from official document number."
        };

        List<string> body = plan.Lines.Select(l =>
                $"{l.SkuOrCode} | {l.Description} | qty {l.Quantity} | {l.LineTotal}")
            .ToList();
        if (body.Count == 0)
        {
            body.Add("(no lines)");
        }

        List<string> footer = new()
        {
            $"Subtotal: {plan.Totals.Subtotal} {plan.Totals.CurrencyCode}",
            $"Tax: {plan.Totals.TaxTotal} {plan.Totals.CurrencyCode}",
            $"Grand total: {plan.Totals.GrandTotal} {plan.Totals.CurrencyCode}",
            OfficialSafetyConstants.InternalWatermark,
            "No official document is issued. No legal output is produced."
        };

        string receiptText = string.Join(Environment.NewLine, header.Concat(body).Concat(footer));

        return new OfficialReceiptPreviewContract(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: OfficialSafetyConstants.InternalWatermark,
            DocumentKind: plan.DocumentKind,
            PreviewOfficialNumber: plan.Numbering.FormattedPreviewNumber,
            OperationalNumberHint: plan.OperationalPosNumberHint,
            HeaderLines: header,
            BodyLines: body,
            FooterLines: footer,
            PlainTextPreview: receiptText,
            OfficialReceiptIssued: false);
    }
}

public static class OfficialInternalXReadingPreviewRenderer
{
    public static OfficialXReadingPreviewContract Render(OfficialDryRunPostingPlan plan)
    {
        string text = string.Join(Environment.NewLine, new[]
        {
            OfficialSafetyConstants.InternalWatermark,
            "X-reading internal preview (shape only).",
            "Does not close shift. Does not reset counters.",
            "Grand total (dry-run): " + plan.Totals.GrandTotal + " " + plan.Totals.CurrencyCode,
            OfficialSafetyConstants.InternalWatermark
        });

        return new OfficialXReadingPreviewContract(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: OfficialSafetyConstants.InternalWatermark,
            PlainTextPreview: text,
            ResetsCounters: false,
            Summary: "X-reading preview is internal shape only; no shift close or counter reset.");
    }
}

public static class OfficialInternalZReadingPreviewRenderer
{
    public static OfficialZReadingPreviewContract Render(OfficialDryRunPostingPlan plan)
    {
        string text = string.Join(Environment.NewLine, new[]
        {
            OfficialSafetyConstants.InternalWatermark,
            "Z-reading internal preview (shape only).",
            "Does not finalize day. Does not reset counters.",
            "Grand total (dry-run): " + plan.Totals.GrandTotal + " " + plan.Totals.CurrencyCode,
            OfficialSafetyConstants.InternalWatermark
        });

        return new OfficialZReadingPreviewContract(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: OfficialSafetyConstants.InternalWatermark,
            PlainTextPreview: text,
            ResetsCounters: false,
            Summary: "Z-reading preview is internal shape only; no day finalization or counter reset.");
    }
}

public static class OfficialInternalJournalExportPreviewRenderer
{
    public static OfficialJournalExportPreviewContract Render(OfficialDryRunPostingPlan plan)
    {
        string text = string.Join(Environment.NewLine, new[]
        {
            OfficialSafetyConstants.InternalWatermark,
            "Electronic journal export preview (shape only).",
            "EJ skeleton present: " + plan.Journal.SkeletonPresent,
            "Live append: false",
            "Export ready: false",
            plan.Journal.Summary,
            OfficialSafetyConstants.InternalWatermark
        });

        return new OfficialJournalExportPreviewContract(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: OfficialSafetyConstants.InternalWatermark,
            PlainTextPreview: text,
            LiveAppended: false,
            ExportReady: false,
            Summary: "EJ export preview is not live-appended and is not export-ready for legal use.");
    }
}

public static class OfficialInternalVoidRefundPreviewRenderer
{
    private static readonly string[] PlaceholderReasons =
    {
        "CUSTOMER_REQUEST", "PRICING_ERROR", "ITEM_RETURN", "DUPLICATE_SALE", "OTHER"
    };

    public static OfficialVoidRefundPreviewContract Render(OfficialDryRunPostingPlan plan)
    {
        string text = string.Join(Environment.NewLine, new[]
        {
            OfficialSafetyConstants.InternalWatermark,
            "Void/refund report preview (shape only).",
            "Live integrated: false",
            "Placeholder reasons: " + string.Join(", ", PlaceholderReasons),
            plan.VoidRefund.Summary,
            OfficialSafetyConstants.InternalWatermark
        });

        return new OfficialVoidRefundPreviewContract(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: OfficialSafetyConstants.InternalWatermark,
            PlainTextPreview: text,
            LiveIntegrated: false,
            PlaceholderReasonCodes: PlaceholderReasons,
            Summary: "Void/refund preview uses placeholders only; no sale is voided or refunded.");
    }
}

public static class OfficialInternalSalesSummaryPreviewRenderer
{
    public static string RenderPlainText(OfficialDryRunPostingPlan plan)
    {
        return string.Join(Environment.NewLine, new[]
        {
            OfficialSafetyConstants.InternalWatermark,
            "Sales summary internal preview (shape only).",
            "Document kind: " + plan.DocumentKind,
            "Line count: " + plan.Lines.Count,
            "Subtotal: " + plan.Totals.Subtotal + " " + plan.Totals.CurrencyCode,
            "Tax: " + plan.Totals.TaxTotal + " " + plan.Totals.CurrencyCode,
            "Grand total: " + plan.Totals.GrandTotal + " " + plan.Totals.CurrencyCode,
            "Not an official sales book. No eSales push. No legal output.",
            OfficialSafetyConstants.InternalWatermark
        });
    }
}
