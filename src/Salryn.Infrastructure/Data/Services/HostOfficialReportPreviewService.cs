using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M8/M9 internal report preview builder.
/// Watermarked text/JSON/internal HTML only. No file export engine. No legal issuance.
/// </summary>
public sealed class HostOfficialReportPreviewService : IHostOfficialReportPreviewService
{
    public const string InternalWatermark = OfficialSafetyConstants.InternalWatermark;

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

        OfficialReceiptPreviewContract receipt = OfficialInternalReceiptPreviewRenderer.Render(plan);
        OfficialXReadingPreviewContract xReading = OfficialInternalXReadingPreviewRenderer.Render(plan);
        OfficialZReadingPreviewContract zReading = OfficialInternalZReadingPreviewRenderer.Render(plan);
        OfficialJournalExportPreviewContract journal = OfficialInternalJournalExportPreviewRenderer.Render(plan);
        OfficialVoidRefundPreviewContract voidRefund = OfficialInternalVoidRefundPreviewRenderer.Render(plan);
        _ = OfficialInternalSalesSummaryPreviewRenderer.RenderPlainText(plan);
        _ = OfficialInternalReportRenderer.ToInternalHtmlPreview(receipt.PlainTextPreview);

        OfficialReportPreviewPackageContract package = new(
            ContractVersion: OfficialContractVersions.M3,
            Watermark: OfficialSafetyConstants.InternalWatermark,
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

        // Keep InternalOnly JSON pathway explicit for gates and serialization predictability.
        _ = JsonSerializer.Serialize(new
        {
            watermark = OfficialSafetyConstants.InternalWatermark,
            dryRunOnly = true,
            format = OfficialReportPreviewFormat.InternalOnly.ToString()
        });

        return Task.FromResult(Result<OfficialReportPreviewPackageContract>.Success(package));
    }
}
