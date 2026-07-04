namespace Salryn.Api.Local.Contracts;

public sealed record RemoteReadOnlyBindPolicyResponse(
    string DefaultMode,
    string EffectiveMode,
    bool DisabledByDefault,
    bool LocalhostOnly,
    bool ExplicitHostBindRequired,
    bool SpecificIpOnly,
    bool NoWildcardBind,
    bool NoDiscovery,
    string Message);

public sealed record RemoteReadOnlyStatusResponse(
    string ConnectionStatus,
    string Mode,
    string Message,
    string? StoreId,
    string? HostId,
    string? TerminalId,
    string? TerminalName,
    string? TerminalRole,
    string? SessionExpiresAtUtc,
    RemoteReadOnlyBindPolicyResponse BindPolicy,
    IReadOnlyList<string> AllowedCapabilities,
    IReadOnlyList<string> DisabledMutations,
    RemoteReadOnlySessionResilienceResponse? SessionResilience = null);

public sealed record RemoteReadOnlyCapabilitiesResponse(
    IReadOnlyList<string> AllowedCapabilities,
    IReadOnlyList<string> DisabledMutations,
    string Message);

public sealed record RemoteReadOnlyTerminalStatusResponse(
    string ConnectionStatus,
    string TerminalStatus,
    string Message,
    string? TerminalId,
    string? TerminalName,
    string? TerminalRole,
    string? SessionExpiresAtUtc,
    RemoteReadOnlySessionResilienceResponse? SessionResilience = null);

// LAN-M15-D: safe, read-only session lifecycle guidance for remote clients. This
// never carries secrets and never instructs automatic re-pair, QR, or discovery.
public sealed record RemoteReadOnlySessionResilienceResponse(
    string LifecycleState,
    string Guidance,
    bool ReadOnlyRefreshSafe,
    bool RepairRequired,
    bool AutomaticRepairDisabled,
    string StaleDataWarning,
    string? DenialCode,
    string? DenialMessage);

public sealed record RemoteReadOnlyAuditItemResponse(
    string OccurredAtUtc,
    string Category,
    string Message,
    string Scope);

public sealed record RemoteReadOnlyAuditVisibilityResponse(
    string Status,
    string Message,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<RemoteReadOnlyAuditItemResponse> Items);

public sealed record RemoteReadOnlyDiagnosticItemResponse(
    string Code,
    string Category,
    string Severity,
    string Message);

public sealed record RemoteReadOnlyDiagnosticsResponse(
    string Mode,
    string OverallStatus,
    string Message,
    bool ReadOnlyOnly,
    bool LanDisabledByDefault,
    bool TerminalIdentityRequired,
    bool HandshakeProofRequired,
    bool CookieTrustAcceptedForRemoteLan,
    bool CorsEnabled,
    bool AuditPolicyAvailable,
    string? LastDenialCode,
    string? LastDenialMessage,
    IReadOnlyList<RemoteReadOnlyDiagnosticItemResponse> Items);

public sealed record RemoteReadOnlyPreflightCheckResponse(
    string Code,
    string Status,
    string Category,
    string Message);

public sealed record RemoteReadOnlyPreflightResponse(
    string OverallReadiness,
    string Message,
    bool ReadOnlyOnly,
    IReadOnlyList<RemoteReadOnlyPreflightCheckResponse> Checks,
    IReadOnlyList<string> SafeNextSteps,
    IReadOnlyList<string> BlockedReasons);

public sealed record LocalLanControlTerminalResponse(
    string TerminalId,
    string TerminalName,
    string TerminalRole,
    string Status,
    bool Trusted,
    string TrustState,
    string RegisteredAtUtc,
    string? LastSeenAtUtc,
    string? DisabledAtUtc,
    string? RevokedAtUtc);

public sealed record LocalLanControlStatusResponse(
    bool LocalOperatorOnly,
    bool RemoteTransportEnabled,
    string BindMode,
    string ConfiguredBindAddress,
    RemoteReadOnlyBindPolicyResponse BindPolicy,
    bool TerminalVisibilityAvailable,
    string TerminalVisibilityMessage,
    IReadOnlyList<LocalLanControlTerminalResponse> Terminals,
    IReadOnlyList<string> Warnings,
    string Message,
    // LAN-M16: host-local, process-scoped remote POS write runtime enablement. Disabled by
    // default and always separate from RemoteTransportEnabled (the read-only bridge toggle).
    bool RemotePosRuntimeEnabled = false);

public sealed record LocalLanControlRemoteTransportRequest(
    bool Enable,
    string? BindAddress);

// LAN-M16: local-only request to enable/disable the host-owned remote POS command runtime.
public sealed record LocalLanControlRemotePosRequest(bool Enable);

public sealed record LocalLanControlTerminalActionResponse(
    string TerminalId,
    string Status,
    string TrustState,
    string Message);
