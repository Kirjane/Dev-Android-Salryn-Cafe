namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M7 official sale posting dry-run planner contracts.
/// Planning package only — never posts, allocates, appends EJ, or generates output.
/// </summary>
public enum OfficialDryRunPostingStatus
{
    Planned = 1,
    Blocked = 2
}

public enum OfficialDryRunSaleSource
{
    SyntheticInternal = 1,
    OperationalSaleReadOnly = 2
}

public sealed record OfficialDryRunBlocker(
    string Code,
    string Message);

public sealed record OfficialDryRunWarning(
    string Code,
    string Message);

public sealed record OfficialDryRunLineItem(
    string SkuOrCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? TaxBucketCode);

public sealed record OfficialDryRunTotals(
    decimal Subtotal,
    decimal TaxTotal,
    decimal GrandTotal,
    string CurrencyCode);

public sealed record OfficialDryRunNumberingPlan(
    bool PreviewOnly,
    bool SequenceWillMutate,
    long? PreviewNextNumber,
    string? FormattedPreviewNumber,
    string? Prefix,
    string Summary);

public sealed record OfficialDryRunJournalPlan(
    bool LiveBound,
    bool EjWillAppendLive,
    bool SkeletonPresent,
    string Summary);

public sealed record OfficialDryRunVoidRefundPlan(
    bool LiveIntegrated,
    bool SkeletonPresent,
    string Summary);

public sealed record OfficialDryRunReportPackagePlan(
    bool ReportDataContractPresent,
    bool ReportEngineSelected,
    bool CrystalSelected,
    bool OutputWillGenerate,
    string Summary);

public sealed record OfficialDryRunPostingRequest(
    string DocumentKind,
    string? OperationalPosNumberHint,
    string? TaxpayerNameHint,
    IReadOnlyList<OfficialDryRunLineItem> Lines,
    OfficialDryRunTotals? Totals);

public sealed record OfficialDryRunPostingPlan(
    int ContractVersion,
    OfficialDryRunPostingStatus Status,
    OfficialDryRunSaleSource SaleSource,
    bool DryRunOnly,
    bool NonOfficial,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool SequenceWillMutate,
    bool SalesWillMutate,
    bool EjWillAppendLive,
    bool OutputWillGenerate,
    bool ComplianceClaimed,
    string DocumentKind,
    string? OperationalPosNumberHint,
    IReadOnlyList<OfficialDryRunLineItem> Lines,
    OfficialDryRunTotals Totals,
    OfficialDryRunNumberingPlan Numbering,
    OfficialDryRunJournalPlan Journal,
    OfficialDryRunVoidRefundPlan VoidRefund,
    OfficialDryRunReportPackagePlan ReportPackage,
    IReadOnlyList<OfficialDryRunBlocker> Blockers,
    IReadOnlyList<OfficialDryRunWarning> Warnings,
    string Summary);
