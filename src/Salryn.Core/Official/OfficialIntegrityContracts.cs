namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M6 combined host official integrity status.
/// Read-only. Never claims compliance or live official readiness.
/// </summary>
public sealed record OfficialIntegrityStatusContract(
    int ContractVersion,
    bool LiveOfficialPostingAllowed,
    bool CheckoutIntegrated,
    bool RemotePosOfficialAllowed,
    bool OfficialOutputImplemented,
    bool ComplianceClaimed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    OfficialJournalStatusContract Journal,
    OfficialVoidRefundStatusContract VoidRefund,
    IReadOnlyList<string> Warnings,
    string Summary);
