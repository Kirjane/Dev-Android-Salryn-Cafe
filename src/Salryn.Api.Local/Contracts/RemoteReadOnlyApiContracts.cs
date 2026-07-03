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
    IReadOnlyList<string> DisabledMutations);

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
    string? SessionExpiresAtUtc);

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
