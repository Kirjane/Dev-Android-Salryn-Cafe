namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M9 guarded live official posting candidate contracts.
/// Always blocked by default. Not production activation. Not checkout-bound.
/// </summary>
public sealed record OfficialLivePostingCandidateRequest(
    Guid? OperationalSaleId,
    string? DocumentKind,
    bool ExplicitInternalActivationRequested);

public sealed record OfficialLivePostingCandidateBlocker(
    string Code,
    string Message);

public sealed record OfficialLivePostingCandidateWarning(
    string Code,
    string Message);

public sealed record OfficialLivePostingCandidateResult(
    int ContractVersion,
    bool CandidateOnly,
    bool DisabledByDefault,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool SequenceWillMutate,
    bool SalesWillMutate,
    bool EjWillAppendLive,
    bool OutputWillGenerate,
    bool OfficialReceiptIssued,
    bool ComplianceClaimed,
    bool ProductionActivation,
    bool Blocked,
    IReadOnlyList<OfficialLivePostingCandidateBlocker> Blockers,
    IReadOnlyList<OfficialLivePostingCandidateWarning> Warnings,
    string Summary);
