namespace Salryn.Core.Official;

/// <summary>
/// Product-level host official readiness (RMO-BIR-M3).
/// Read-only. Never claims compliance or allows live official posting.
/// </summary>
public enum HostOfficialReadinessStatus
{
    Unknown = 0,
    Disabled = 1,
    NotReady = 2,

    /// <summary>
    /// Config looks complete enough for internal engineering dry-run of readiness/contracts.
    /// Does <b>not</b> mean live official posting is allowed.
    /// </summary>
    ReadyForInternalDryRun = 3,

    /// <summary>Required setup fields incomplete. Not live-ready.</summary>
    SetupIncomplete = 4,

    /// <summary>Required setup filled for internal dry-run only. Not live-ready / not compliant.</summary>
    ConfiguredForInternalDryRun = 5
}

public sealed record HostOfficialReadinessItem(
    string Code,
    string Message);

public sealed record HostOfficialReadinessSnapshot(
    int ContractVersion,
    HostOfficialReadinessStatus Status,
    bool OfficialOutputEnabledInConfig,
    bool LiveOfficialPostingAllowed,
    string ComplianceMode,
    IReadOnlyList<HostOfficialReadinessItem> Blockers,
    IReadOnlyList<HostOfficialReadinessItem> Warnings,
    IReadOnlyList<string> AllowedActions,
    OfficialBusinessProfileSnapshot? BusinessProfile,
    OfficialMachineProfileSnapshot? MachineProfile,
    OfficialDocumentSeriesContract? ActiveSeriesHint,
    string Summary);
