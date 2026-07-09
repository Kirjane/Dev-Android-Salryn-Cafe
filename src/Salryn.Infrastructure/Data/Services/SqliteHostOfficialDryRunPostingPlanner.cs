using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M7 official sale dry-run planner.
/// Synthetic package only. Never mutates sequence/sales/EJ. Never generates output.
/// </summary>
public sealed class SqliteHostOfficialDryRunPostingPlanner : IHostOfficialDryRunPostingPlanner
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteHostOfficialDryRunPostingPlanner(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<OfficialDryRunPostingPlan>> BuildPlanAsync(
        OfficialDryRunPostingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is null)
        {
            return Result<OfficialDryRunPostingPlan>.Fail(Error.Validation(
                "HostOfficialDryRun.RequestRequired",
                "Dry-run posting request is required."));
        }

        List<OfficialDryRunBlocker> blockers = new();
        List<OfficialDryRunWarning> warnings = new()
        {
            new("HostOfficialDryRun.InternalOnly", "Internal dry-run only."),
            new("HostOfficialDryRun.NoOfficialReceipt", "No official receipt is issued."),
            new("HostOfficialDryRun.CheckoutDisconnected", "Checkout is not connected to official numbering."),
            new("HostOfficialDryRun.NoCompliance", "No BIR/RMO compliance is claimed."),
            new(
                "HostOfficialDryRun.DualNumbering",
                "Operational POS number is separate from official document number; official number is preview only.")
        };

        string documentKind = string.IsNullOrWhiteSpace(request.DocumentKind)
            ? "OFFICIAL_INVOICE"
            : request.DocumentKind.Trim();

        if (!string.Equals(documentKind, "OFFICIAL_INVOICE", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(documentKind, "OFFICIAL_RECEIPT", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new OfficialDryRunBlocker(
                "HostOfficialDryRun.DocumentKindInvalid",
                "Document kind must be OFFICIAL_INVOICE or OFFICIAL_RECEIPT for dry-run planning."));
        }

        IReadOnlyList<OfficialDryRunLineItem> lines = request.Lines ?? Array.Empty<OfficialDryRunLineItem>();
        if (lines.Count == 0)
        {
            lines = new[]
            {
                new OfficialDryRunLineItem(
                    SkuOrCode: "DRY-RUN-1",
                    Description: "Synthetic dry-run line",
                    Quantity: 1m,
                    UnitPrice: 100m,
                    LineTotal: 100m,
                    TaxBucketCode: "VATABLE")
            };
            warnings.Add(new OfficialDryRunWarning(
                "HostOfficialDryRun.SyntheticLineUsed",
                "No lines provided; a synthetic dry-run line was inserted for package shape only."));
        }

        OfficialDryRunTotals totals = request.Totals ?? new OfficialDryRunTotals(
            Subtotal: lines.Sum(l => l.LineTotal),
            TaxTotal: 0m,
            GrandTotal: lines.Sum(l => l.LineTotal),
            CurrencyCode: "PHP");

        OfficialNumberingPreviewContract? numberingPreview = null;
        try
        {
            IHostOfficialNumberingService numbering = new SqliteHostOfficialNumberingService(_connectionFactory);
            Result<OfficialNumberingPreviewContract> numberingResult =
                await numbering.GetPreviewAsync(cancellationToken).ConfigureAwait(false);
            if (numberingResult.IsSuccess)
            {
                numberingPreview = numberingResult.Value;
            }
            else
            {
                blockers.Add(new OfficialDryRunBlocker(
                    "HostOfficialDryRun.NumberingPreviewUnavailable",
                    numberingResult.ErrorDetail.Message));
            }
        }
        catch (Exception ex)
        {
            blockers.Add(new OfficialDryRunBlocker(
                "HostOfficialDryRun.NumberingPreviewFailed",
                "Numbering preview failed: " + ex.Message));
        }

        bool ejSkeleton = false;
        bool voidSkeleton = false;
        try
        {
            IHostOfficialIntegrityService integrity = new SqliteHostOfficialIntegrityService(_connectionFactory);
            Result<OfficialIntegrityStatusContract> integrityResult =
                await integrity.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (integrityResult.IsSuccess)
            {
                ejSkeleton = integrityResult.Value.Journal.SkeletonPresent;
                voidSkeleton = integrityResult.Value.VoidRefund.SkeletonPresent;
            }
        }
        catch
        {
            // Integrity probe failure does not mutate; report via warning.
            warnings.Add(new OfficialDryRunWarning(
                "HostOfficialDryRun.IntegrityProbeDegraded",
                "Integrity probe degraded; plan continues with not-live-bound defaults."));
        }

        OfficialDryRunNumberingPlan numberingPlan = new(
            PreviewOnly: true,
            SequenceWillMutate: false,
            PreviewNextNumber: numberingPreview?.NextNumber,
            FormattedPreviewNumber: numberingPreview?.FormattedNextNumber,
            Prefix: numberingPreview?.Prefix,
            Summary: numberingPreview is null
                ? "Official numbering preview unavailable; no sequence mutation occurs."
                : "Official number is preview only; sequence will not mutate; no legal document is issued.");

        OfficialDryRunJournalPlan journalPlan = new(
            LiveBound: false,
            EjWillAppendLive: false,
            SkeletonPresent: ejSkeleton,
            Summary: ejSkeleton
                ? "EJ skeleton present; live append will not run for this dry-run plan."
                : "EJ skeleton missing or unknown; live append will not run.");

        OfficialDryRunVoidRefundPlan voidPlan = new(
            LiveIntegrated: false,
            SkeletonPresent: voidSkeleton,
            Summary: "Void/refund integrity remains not live-integrated; dry-run plan does not void or refund.");

        OfficialDryRunReportPackagePlan reportPlan = new(
            ReportDataContractPresent: true,
            ReportEngineSelected: false,
            CrystalSelected: false,
            OutputWillGenerate: false,
            Summary: "Report data handoff contracts are defined; no report engine selected; no output generated.");

        // Always keep product-spine blockers on the plan (even if package shape is valid).
        blockers.Add(new OfficialDryRunBlocker(
            "HostOfficialDryRun.LiveCheckoutNotImplemented",
            "Live official checkout is not implemented."));
        blockers.Add(new OfficialDryRunBlocker(
            "HostOfficialDryRun.OutputEngineMissing",
            "Official output engine is missing; no receipt/PDF/printer/Crystal generation."));

        OfficialDryRunPostingStatus status = OfficialDryRunPostingStatus.Planned;

        string summary =
            "Dry-run official posting plan built for engineering only. " +
            "No official document is issued. No legal output is produced. " +
            "Operational POS number remains separate from official document number.";

        return Result<OfficialDryRunPostingPlan>.Success(new OfficialDryRunPostingPlan(
            ContractVersion: OfficialContractVersions.M3,
            Status: status,
            SaleSource: OfficialDryRunSaleSource.SyntheticInternal,
            DryRunOnly: true,
            NonOfficial: true,
            LiveOfficialPostingAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            SequenceWillMutate: false,
            SalesWillMutate: false,
            EjWillAppendLive: false,
            OutputWillGenerate: false,
            ComplianceClaimed: false,
            DocumentKind: documentKind,
            OperationalPosNumberHint: request.OperationalPosNumberHint,
            Lines: lines,
            Totals: totals,
            Numbering: numberingPlan,
            Journal: journalPlan,
            VoidRefund: voidPlan,
            ReportPackage: reportPlan,
            Blockers: blockers,
            Warnings: warnings,
            Summary: summary));
    }
}
