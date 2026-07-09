namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M8 internal dry-run package contracts. Not legal issuance.
/// </summary>
public enum OfficialDryRunPackageSourceMode
{
    Synthetic = 1,
    OperationalSaleReadOnly = 2,
    Unknown = 3
}

public enum OfficialDryRunPackagePersistenceStatus
{
    Created = 1,
    PreviewBuilt = 2
}

public sealed record OfficialDryRunPackageFlags(
    bool DryRunOnly,
    bool NonOfficial,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool SequenceWillMutate,
    bool SalesWillMutate,
    bool EjWillAppendLive,
    bool OutputWillGenerate,
    bool OfficialReceiptIssued,
    bool ComplianceClaimed);

public sealed record OfficialDryRunPackageWarning(
    string Code,
    string Message);

public sealed record OfficialDryRunPackageBlocker(
    string Code,
    string Message);

public sealed record OfficialDryRunPackageSummaryContract(
    Guid Id,
    string PublicId,
    DateTimeOffset CreatedAtUtc,
    OfficialDryRunPackageSourceMode SourceMode,
    OfficialDryRunPackagePersistenceStatus Status,
    string? Summary);

public sealed record OfficialDryRunPackageContract(
    int ContractVersion,
    Guid Id,
    string PublicId,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByUserId,
    OfficialDryRunPackageSourceMode SourceMode,
    Guid? SourceSaleId,
    OfficialDryRunPackagePersistenceStatus Status,
    OfficialDryRunPackageFlags Flags,
    OfficialDryRunPostingPlan Plan,
    OfficialReportPreviewPackageContract? ReportPreview,
    IReadOnlyList<OfficialDryRunPackageWarning> Warnings,
    IReadOnlyList<OfficialDryRunPackageBlocker> Blockers,
    string Summary);

public sealed record OfficialDryRunPackageCreateRequest(
    OfficialDryRunPostingRequest? PostingRequest);
