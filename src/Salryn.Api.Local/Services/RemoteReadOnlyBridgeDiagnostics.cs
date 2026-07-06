using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Contracts;
using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

public static class RemoteReadOnlyBridgeDiagnostics
{
    public const string LocalOnlyMode = "RemoteReadOnly.LocalOnlyMode";

    public const string RemoteTransportDisabled = "RemoteReadOnly.RemoteTransportDisabled";

    public const string ExplicitLanBindNotConfigured = "RemoteReadOnly.ExplicitLanBindNotConfigured";

    public const string ForbiddenBindAddressRejected = "RemoteReadOnly.ForbiddenBindAddressRejected";

    public const string TerminalHeaderMissing = "RemoteReadOnlyBridge.TerminalHeaderMissing";

    public const string HandshakeHeaderMissing = "RemoteReadOnlyBridge.HandshakeHeaderMissing";

    public const string TerminalUnknown = "RemoteReadOnlyBridge.TerminalUnknown";

    public const string TerminalPending = "LanRemoteTransport.PendingTerminalRejected";

    public const string TerminalDisabled = "LanRemoteTransport.DisabledTerminalRejected";

    public const string TerminalRevoked = "LanRemoteTransport.RevokedTerminalRejected";

    public const string HandshakeMissing = "RemoteReadOnlyBridge.HandshakeMissing";

    public const string HandshakeExpiredOrInvalid = "RemoteHandshake.SessionInvalid";

    public const string UnsafeCategoryDenied = "RemoteReadOnly.UnsafeCategoryDenied";

    public const string MutationDenied = "RemoteReadOnly.MutationDenied";

    public const string OfficialWriteDenied = "RemoteReadOnly.OfficialWriteDenied";

    public const string CookieTrustRejectedForRemoteLan = "RemoteReadOnlyBridge.CookieTrustRejected";

    public const string CorsNotEnabled = "RemoteReadOnly.CorsNotEnabled";

    public const string AuditPolicyAvailable = "RemoteReadOnly.AuditPolicyAvailable";

    public const string AuditPolicyUnavailable = "RemoteReadOnly.AuditPolicyUnavailable";

    public const string HybridBridgeReady = "RemoteReadOnly.HybridBridgeReady";

    public static RemoteReadOnlyDiagnosticsResponse BuildOperatorDiagnostics(
        LanRemoteTransportConfig config,
        bool auditPolicyAvailable,
        RemoteReadOnlyBridgeRequestContext? bridgeRequest = null,
        string? lastDenialCode = null,
        string? lastDenialMessage = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        List<RemoteReadOnlyDiagnosticItemResponse> items = new();
        string mode = RemoteReadOnlyLanBindPolicyMapper.ResolveConnectionMode(config);

        if (!config.RemoteTransportEnabled)
        {
            items.Add(Item(
                RemoteTransportDisabled,
                "transport",
                "info",
                "Remote transport is disabled by default. Explicit operator enable is required."));
            items.Add(Item(
                LocalOnlyMode,
                "mode",
                "info",
                "Api.Local is operating in local-only read mode on 127.0.0.1."));
        }

        if (config.RemoteTransportEnabled
            && config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            items.Add(Item(
                ExplicitLanBindNotConfigured,
                "bind",
                "warning",
                "Remote transport is enabled but explicit LAN bind is not configured."));
        }

        if (!string.IsNullOrWhiteSpace(config.BindAddress)
            && SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(config.BindAddress))
        {
            items.Add(Item(
                ForbiddenBindAddressRejected,
                "bind",
                "error",
                "Configured bind address is forbidden. Wildcard and " + "0.0." + "0.0 bind remain rejected."));
        }

        if (config.RemoteTransportEnabled
            && config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            items.Add(Item(
                HybridBridgeReady,
                "mode",
                "info",
                "Hybrid bridge is configured. Remote reads require terminal identity and handshake proof."));
        }

        items.Add(Item(
            TerminalHeaderMissing,
            "trust",
            "info",
            "Remote LAN reads require the X-Salryn-Terminal-Id header."));
        items.Add(Item(
            HandshakeHeaderMissing,
            "trust",
            "info",
            "Remote LAN reads require the X-Salryn-Handshake-Session-Id header."));
        items.Add(Item(
            CookieTrustRejectedForRemoteLan,
            "trust",
            "warning",
            "Localhost operator session cookies are not accepted as remote LAN trust."));
        items.Add(Item(
            CorsNotEnabled,
            "network",
            "info",
            "CORS is not enabled for remote-readonly endpoints."));
        items.Add(Item(
            MutationDenied,
            "policy",
            "warning",
            "Mutation, checkout, admin, backup, settings, and official output remain denied for remote clients."));
        items.Add(Item(
            OfficialWriteDenied,
            "policy",
            "warning",
            "Official journal writes and official sequence allocation remain denied for remote clients."));
        items.Add(Item(
            auditPolicyAvailable ? AuditPolicyAvailable : AuditPolicyUnavailable,
            "audit",
            auditPolicyAvailable ? "info" : "warning",
            auditPolicyAvailable
                ? "Read-attempt audit visibility is available through the read-only audit endpoint."
                : "Read-attempt audit visibility is unavailable until audit reads succeed."));

        if (bridgeRequest is not null)
        {
            AppendRequestDiagnostics(items, bridgeRequest);
        }

        string overallStatus = config.RemoteTransportEnabled
            && config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress
            ? "HybridBridgeReady"
            : "LocalOnlyReadMode";

        return new RemoteReadOnlyDiagnosticsResponse(
            Mode: mode,
            OverallStatus: overallStatus,
            Message: SanitizePublicText(
                "Remote read-only LAN diagnostics are safe for operator display and exclude secrets.")
                ?? "Remote read-only LAN diagnostics are safe for operator display and exclude secrets.",
            ReadOnlyOnly: true,
            LanDisabledByDefault: !config.RemoteTransportEnabled,
            TerminalIdentityRequired: true,
            HandshakeProofRequired: true,
            CookieTrustAcceptedForRemoteLan: false,
            CorsEnabled: false,
            AuditPolicyAvailable: auditPolicyAvailable,
            LastDenialCode: SanitizePublicText(lastDenialCode),
            LastDenialMessage: SanitizePublicText(lastDenialMessage),
            Items: items);
    }

    public static (string Code, string Message, string AuditReason) ResolveBridgeDenial(
        RemoteReadOnlyBridgeRequestContext bridgeRequest)
    {
        ArgumentNullException.ThrowIfNull(bridgeRequest);

        if (!bridgeRequest.IsLoopbackRequest
            && bridgeRequest.HasOperatorSessionCookie
            && bridgeRequest.TerminalId == Guid.Empty
            && bridgeRequest.HandshakeSessionId == Guid.Empty)
        {
            return (
                CookieTrustRejectedForRemoteLan,
                "Localhost operator session cookies cannot authorize remote LAN bridge reads.",
                "cookie trust rejected");
        }

        if (bridgeRequest.TerminalId == Guid.Empty)
        {
            if (!bridgeRequest.HasTerminalHeader)
            {
                return (
                    TerminalHeaderMissing,
                    "Remote bridge requests require the X-Salryn-Terminal-Id header.",
                    "terminal header missing");
            }

            return (
                TerminalUnknown,
                "Remote terminal identity is unknown or invalid.",
                "terminal unknown");
        }

        if (bridgeRequest.HandshakeSessionId == Guid.Empty)
        {
            if (!bridgeRequest.HasHandshakeHeader)
            {
                return (
                    HandshakeHeaderMissing,
                    "Remote bridge requests require the X-Salryn-Handshake-Session-Id header.",
                    "handshake header missing");
            }

            return (
                HandshakeMissing,
                "Remote handshake session proof is missing or invalid.",
                "handshake missing");
        }

        return (
            HandshakeMissing,
            "Remote bridge requests require terminal identity and handshake session headers.",
            "missing handshake");
    }

    public static (string Code, string Message, string AuditReason) MapHandshakeFailure(string handshakeErrorCode)
    {
        string normalized = handshakeErrorCode.Trim();

        return normalized switch
        {
            "RemoteHandshake.SessionInvalid" => (
                HandshakeExpiredOrInvalid,
                "Remote handshake session is invalid or expired.",
                "handshake expired or invalid"),
            "RemoteHandshake.SessionRequired" => (
                HandshakeMissing,
                "Remote handshake session proof is required.",
                "handshake missing"),
            "RemoteHandshake.RemoteDisabled" => (
                RemoteTransportDisabled,
                "Remote read-only bridge is disabled.",
                "remote disabled"),
            "LanRemoteTransport.PendingTerminalRejected" => (
                TerminalPending,
                "Remote terminal is pending pairing and cannot read yet.",
                "terminal pending"),
            "LanRemoteTransport.DisabledTerminalRejected" => (
                TerminalDisabled,
                "Remote terminal is disabled.",
                "terminal disabled"),
            "LanRemoteTransport.RevokedTerminalRejected" => (
                TerminalRevoked,
                "Remote terminal is revoked.",
                "terminal revoked"),
            "LanRemoteTransport.UnknownTerminalRejected" => (
                TerminalUnknown,
                "Remote terminal identity is unknown.",
                "terminal unknown"),
            "LanRemoteTransport.WildcardBindForbidden" => (
                ForbiddenBindAddressRejected,
                "Wildcard bind addresses are forbidden.",
                "forbidden bind address rejected"),
            _ => (
                normalized,
                "Remote bridge authorization failed.",
                SanitizePublicText(normalized) ?? "authorization denied")
        };
    }

    public static string? SanitizePublicText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        if (ContainsSecretMarker(trimmed))
        {
            return "[redacted]";
        }

        return trimmed.Length <= 256 ? trimmed : trimmed[..256];
    }

    public static string SanitizeRemoteClientMessage(
        string? value,
        string fallback = "Remote request could not be completed.")
    {
        string? sanitized = SanitizePublicText(value);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        if (sanitized.Contains("handler failed", StringComparison.OrdinalIgnoreCase)
            || sanitized.Contains("InnerException", StringComparison.OrdinalIgnoreCase)
            || sanitized.Contains("StackTrace", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return sanitized;
    }

    public static bool ContainsSecretMarker(string value) =>
        value.Contains("cookie", StringComparison.OrdinalIgnoreCase)
        || value.Contains("token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("password", StringComparison.OrdinalIgnoreCase)
        || value.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || value.Contains("salryn.sid", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".db", StringComparison.OrdinalIgnoreCase)
        || value.Contains("sqlite", StringComparison.OrdinalIgnoreCase)
        || value.Contains("connection string", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase);

    private static void AppendRequestDiagnostics(
        List<RemoteReadOnlyDiagnosticItemResponse> items,
        RemoteReadOnlyBridgeRequestContext bridgeRequest)
    {
        if (!bridgeRequest.IsLoopbackRequest)
        {
            items.Add(Item(
                "RemoteReadOnly.NonLoopbackBridgeRequest",
                "request",
                "info",
                "Request is classified as a remote bridge request."));
        }

        if (bridgeRequest.HasOperatorSessionCookie && !bridgeRequest.IsLoopbackRequest)
        {
            items.Add(Item(
                CookieTrustRejectedForRemoteLan,
                "request",
                "warning",
                "Operator session cookie was present on a non-loopback request and is not remote LAN trust."));
        }

        if (!bridgeRequest.HasTerminalHeader)
        {
            items.Add(Item(
                TerminalHeaderMissing,
                "request",
                "error",
                "Terminal identity header is missing from this request."));
        }

        if (!bridgeRequest.HasHandshakeHeader)
        {
            items.Add(Item(
                HandshakeHeaderMissing,
                "request",
                "error",
                "Handshake session header is missing from this request."));
        }
    }

    private static RemoteReadOnlyDiagnosticItemResponse Item(
        string code,
        string category,
        string severity,
        string message) =>
        new(
            Code: code,
            Category: category,
            Severity: severity,
            Message: SanitizePublicText(message) ?? string.Empty);
}
