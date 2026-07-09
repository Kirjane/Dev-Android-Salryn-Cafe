using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M8 internal report preview builder.
/// Watermarked text/JSON only. No file export engine. No legal issuance.
/// </summary>
public sealed class HostOfficialReportPreviewService : IHostOfficialReportPreviewService
{
    public const string InternalWatermark =
        "INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE";

    public Task<Result<OfficialReportPreviewPackageContract>> BuildPreviewAsync(
        OfficialDryRunPostingPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plan is null)
        {
            return Task.FromResult(Result<OfficialReportPreviewPackageContract>.Fail(Error.Validation(
                "HostOfficialReportPreview.PlanRequired",
                "Dry-run plan is required to build an internal report preview.")));
        }

        string number = string.IsNullOrWhiteSpace(plan.Numbering.FormattedPreviewNumber)
            ? "(preview blocked)"
            : plan.Numbering.FormattedPreviewNumber!;

        List<string> header = new()
        {
            InternalWatermark,
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
            InternalWatermark,
            "No official document is issued. No legal output is produced."
        };

        string receiptText = string.Join(Environment.NewLine, header.Concat(body).Concat(footer));

        OfficialReceiptPreviewContract receipt = new(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: InternalWatermark,
            DocumentKind: plan.DocumentKind,
            PreviewOfficialNumber: plan.Numbering.FormattedPreviewNumber,
            OperationalNumberHint: plan.OperationalPosNumberHint,
            HeaderLines: header,
            BodyLines: body,
            FooterLines: footer,
            PlainTextPreview: receiptText,
            OfficialReceiptIssued: false);

        OfficialXReadingPreviewContract xReading = new(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: InternalWatermark,
            PlainTextPreview: string.Join(Environment.NewLine, new[]
            {
                InternalWatermark,
                "X-reading internal preview (shape only).",
                "Does not close shift. Does not reset counters.",
                "Grand total (dry-run): " + plan.Totals.GrandTotal + " " + plan.Totals.CurrencyCode,
                InternalWatermark
            }),
            ResetsCounters: false,
            Summary: "X-reading preview is internal shape only; no shift close or counter reset.");

        OfficialZReadingPreviewContract zReading = new(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: InternalWatermark,
            PlainTextPreview: string.Join(Environment.NewLine, new[]
            {
                InternalWatermark,
                "Z-reading internal preview (shape only).",
                "Does not finalize day. Does not reset counters.",
                "Grand total (dry-run): " + plan.Totals.GrandTotal + " " + plan.Totals.CurrencyCode,
                InternalWatermark
            }),
            ResetsCounters: false,
            Summary: "Z-reading preview is internal shape only; no day finalization or counter reset.");

        OfficialJournalExportPreviewContract journal = new(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: InternalWatermark,
            PlainTextPreview: string.Join(Environment.NewLine, new[]
            {
                InternalWatermark,
                "Electronic journal export preview (shape only).",
                "EJ skeleton present: " + plan.Journal.SkeletonPresent,
                "Live append: false",
                "Export ready: false",
                plan.Journal.Summary,
                InternalWatermark
            }),
            LiveAppended: false,
            ExportReady: false,
            Summary: "EJ export preview is not live-appended and is not export-ready for legal use.");

        string[] reasons = { "CUSTOMER_REQUEST", "PRICING_ERROR", "ITEM_RETURN", "DUPLICATE_SALE", "OTHER" };
        OfficialVoidRefundPreviewContract voidRefund = new(
            ContractVersion: OfficialContractVersions.M3,
            Format: OfficialReportPreviewFormat.PlainText,
            Watermark: InternalWatermark,
            PlainTextPreview: string.Join(Environment.NewLine, new[]
            {
                InternalWatermark,
                "Void/refund report preview (shape only).",
                "Live integrated: false",
                "Placeholder reasons: " + string.Join(", ", reasons),
                plan.VoidRefund.Summary,
                InternalWatermark
            }),
            LiveIntegrated: false,
            PlaceholderReasonCodes: reasons,
            Summary: "Void/refund preview uses placeholders only; no sale is voided or refunded.");

        OfficialReportPreviewPackageContract package = new(
            ContractVersion: OfficialContractVersions.M3,
            Watermark: InternalWatermark,
            DryRunOnly: true,
            NonOfficial: true,
            OfficialReceiptIssued: false,
            LiveOfficialPostingAllowed: false,
            OutputWillGenerate: false,
            ReportEngineSelected: false,
            ComplianceClaimed: false,
            Receipt: receipt,
            XReading: xReading,
            ZReading: zReading,
            JournalExport: journal,
            VoidRefund: voidRefund,
            Summary:
                "Internal report preview package built from dry-run plan. " +
                "Not an official receipt or invoice. No report engine selected. No legal output.");

        // Touch Json serializer to keep InternalOnly Json format pathway explicit for gates.
        _ = JsonSerializer.Serialize(new { watermark = InternalWatermark, dryRunOnly = true });

        return Task.FromResult(Result<OfficialReportPreviewPackageContract>.Success(package));
    }
}
