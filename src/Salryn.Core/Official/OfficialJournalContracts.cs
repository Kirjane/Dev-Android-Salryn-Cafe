namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M6 electronic journal integrity contracts (status/plan only).
/// Not live append. Not official output. Not compliance.
/// </summary>
public enum OfficialJournalEventType
{
    SalePosted = 1,
    OfficialNumberPreviewed = 2,
    OfficialNumberAllocated = 3,
    ReprintRequested = 4,
    VoidRequested = 5,
    VoidPosted = 6,
    RefundRequested = 7,
    RefundPosted = 8,
    SetupChanged = 9,
    ReadinessViewed = 10,
    ExportRequested = 11
}

public sealed record OfficialJournalBlocker(
    string Code,
    string Message);

public sealed record OfficialJournalWarning(
    string Code,
    string Message);

public sealed record OfficialJournalEventCapabilityContract(
    OfficialJournalEventType EventType,
    string EventTypeCode,
    bool PlannedForOfficialSpine,
    bool LiveAppendSupported);

public sealed record OfficialJournalAppendPlanContract(
    int ContractVersion,
    bool LiveAppendEnabled,
    bool CheckoutBound,
    bool NonOfficial,
    bool InternalDryRun,
    bool LiveBound,
    bool HashChainPresent,
    bool ExportReady,
    IReadOnlyList<OfficialJournalEventCapabilityContract> PlannedEventTypes,
    string Summary);

public sealed record OfficialJournalStatusContract(
    int ContractVersion,
    bool SchemaPresent,
    bool SkeletonPresent,
    bool LiveBound,
    bool LiveAppendEnabled,
    bool CheckoutIntegrated,
    bool HashChainPresent,
    bool ExportReady,
    bool LiveOfficialPostingAllowed,
    bool RemotePosAllowed,
    bool ComplianceClaim,
    long? EntryRowCount,
    long? EventRowCount,
    OfficialJournalAppendPlanContract AppendPlan,
    IReadOnlyList<OfficialJournalBlocker> Blockers,
    IReadOnlyList<OfficialJournalWarning> Warnings,
    string Summary);
