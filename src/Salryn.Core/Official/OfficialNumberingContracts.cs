namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M5 official numbering preview/validation contracts.
/// Skeleton only — does not issue official numbers, post sales, or claim compliance.
/// </summary>
public enum OfficialNumberingAllocationMode
{
    /// <summary>Read-only next-number computation. Does not mutate sequence.</summary>
    PreviewOnly = 1,

    /// <summary>Reserved for a future internal dry-run that mutates sequence. Not implemented in M5.</summary>
    InternalDryRun = 2,

    /// <summary>Live official allocation is blocked / not productized.</summary>
    LiveOfficialBlocked = 3
}

public sealed record OfficialNumberingBlocker(
    string Code,
    string Message);

public sealed record OfficialNumberingSeriesStateContract(
    Guid? SeriesId,
    Guid? BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    string Prefix,
    int Year,
    long StartNumber,
    long CurrentNumber,
    long? EndNumber,
    int PaddingDigits,
    bool IsActive,
    bool IsExhausted);

public sealed record OfficialNumberingPreviewContract(
    int ContractVersion,
    string DocumentType,
    string Prefix,
    long CurrentNumber,
    long NextNumber,
    string FormattedNextNumber,
    long? RangeStart,
    long? RangeEnd,
    string SequenceScopeCode,
    Guid? BirMachineProfileId,
    Guid? SeriesId,
    bool DryRunOnly,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool PreviewMutatesSequence,
    bool AllocationAllowed,
    OfficialNumberingAllocationMode Mode,
    OfficialNumberingSeriesStateContract? Series,
    IReadOnlyList<OfficialNumberingBlocker> Blockers,
    IReadOnlyList<string> Warnings,
    string Summary,
    string? BlockedReason);

public sealed record OfficialNumberingValidationResult(
    int ContractVersion,
    bool IsValidForPreview,
    bool LiveAllocationAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool LiveOfficialPostingAllowed,
    OfficialNumberingSeriesStateContract? Series,
    long? PreviewNextNumber,
    string? FormattedNextNumber,
    IReadOnlyList<OfficialNumberingBlocker> Blockers,
    IReadOnlyList<string> Warnings,
    string Summary);

public sealed record OfficialNumberingAllocationRequest(
    OfficialNumberingAllocationMode Mode,
    string? DocumentKind,
    Guid? BirMachineProfileId,
    int? Year);

public sealed record OfficialNumberingAllocationResult(
    int ContractVersion,
    OfficialNumberingAllocationMode Mode,
    bool Allocated,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    long? Number,
    string? FormattedDocumentNumber,
    IReadOnlyList<OfficialNumberingBlocker> Blockers,
    string Summary);
