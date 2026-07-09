namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M7 aggregated pre-live spine status. Not compliance. Not live posting.
/// </summary>
public enum OfficialSpineComponentName
{
    SetupProfile = 1,
    DocumentSeries = 2,
    NumberingPreview = 3,
    IntegritySkeleton = 4,
    ElectronicJournalSkeleton = 5,
    VoidRefundSkeleton = 6,
    DryRunPlanner = 7,
    ReportDataContract = 8,
    OfficialOutputEngine = 9,
    LiveCheckoutBinding = 10
}

public enum OfficialSpineReadinessLevel
{
    Missing = 0,
    SkeletonPresent = 1,
    PreviewReady = 2,
    DryRunReady = 3,
    LiveBound = 4
}

public sealed record OfficialSpineBlocker(
    string Code,
    string Message);

public sealed record OfficialSpineWarning(
    string Code,
    string Message);

public sealed record OfficialSpineComponentStatusContract(
    OfficialSpineComponentName Component,
    OfficialSpineReadinessLevel Level,
    bool Present,
    bool LiveBound,
    string Summary);

public sealed record OfficialSpineStatusContract(
    int ContractVersion,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool OfficialOutputImplemented,
    bool ComplianceClaimed,
    bool DryRunOnly,
    bool NonOfficial,
    IReadOnlyList<OfficialSpineComponentStatusContract> Components,
    IReadOnlyList<OfficialSpineBlocker> Blockers,
    IReadOnlyList<OfficialSpineWarning> Warnings,
    string Summary);
