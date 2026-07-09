namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M6 void/refund integrity contracts (status/validation only).
/// Not live void/refund posting. Not compliance.
/// </summary>
public sealed record OfficialVoidRefundBlocker(
    string Code,
    string Message);

public sealed record OfficialVoidRefundWarning(
    string Code,
    string Message);

public sealed record OfficialVoidRefundReasonContract(
    string ReasonCode,
    string DisplayLabel,
    bool IsPlaceholder,
    bool LiveEnforced);

public sealed record OfficialVoidRefundLinkStatusContract(
    int ContractVersion,
    bool OriginalLinkRequired,
    bool CorrectionDocumentRequired,
    bool LiveLinkingImplemented,
    string Summary);

public sealed record OfficialVoidRefundValidationResult(
    int ContractVersion,
    bool SkeletonValid,
    bool LiveIntegrationAllowed,
    IReadOnlyList<OfficialVoidRefundBlocker> Blockers,
    IReadOnlyList<OfficialVoidRefundWarning> Warnings,
    string Summary);

public sealed record OfficialVoidRefundStatusContract(
    int ContractVersion,
    bool SkeletonPresent,
    bool LiveIntegrated,
    bool CheckoutIntegrated,
    bool OperationalVoidExistsSeparately,
    bool ReasonCatalogLiveEnforced,
    bool LiveOfficialPostingAllowed,
    bool RemotePosAllowed,
    bool ComplianceClaim,
    OfficialVoidRefundLinkStatusContract LinkStatus,
    IReadOnlyList<OfficialVoidRefundReasonContract> PlaceholderReasons,
    OfficialVoidRefundValidationResult Validation,
    IReadOnlyList<OfficialVoidRefundBlocker> Blockers,
    IReadOnlyList<OfficialVoidRefundWarning> Warnings,
    string Summary);
