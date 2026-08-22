using Salryn.Api.Local.Contracts;

namespace Salryn.Api.Local.Services;

/// <summary>
/// LAN-M15-D: pure, side-effect-free mapper that turns a safe remote read-only
/// denial code (or message) into operator-facing session lifecycle guidance.
///
/// The mapper is deliberately read-only and fail-closed:
/// * it never emits secrets (all text is passed through <see cref="RemoteReadOnlyBridgeDiagnostics.SanitizePublicText"/>),
/// * it never instructs automatic re-pair, QR, discovery, or network scan,
/// * re-pairing is always described as a manual operation on the POS host.
/// </summary>
public static class RemoteReadOnlySessionLifecycle
{
    public const string SessionActive = "SessionActive";
    public const string LocalOnlyReadMode = "LocalOnlyReadMode";
    public const string HandshakeExpired = "HandshakeExpired";
    public const string HandshakeMissing = "HandshakeMissing";
    public const string TerminalPending = "TerminalPending";
    public const string TerminalDisabled = "TerminalDisabled";
    public const string TerminalRevoked = "TerminalRevoked";
    public const string TerminalUnknown = "TerminalUnknown";
    public const string RemoteDisabled = "RemoteDisabled";
    public const string CookieRejected = "CookieRejected";
    public const string Unknown = "Unknown";

    public const string StaleDataWarning =
        "Displayed values may be stale after a denied or failed read. Use Refresh read-only state to request a fresh safe read. Automatic retry, re-pair, and discovery are disabled to avoid request floods.";

    public static RemoteReadOnlySessionResilienceResponse Active()
    {
        return Build(
            SessionActive,
            "Read-only session is active. Displayed values reflect the last successful safe read.",
            readOnlyRefreshSafe: true,
            repairRequired: false,
            denialCode: null,
            denialMessage: null);
    }

    public static RemoteReadOnlySessionResilienceResponse LocalOnly()
    {
        return Build(
            LocalOnlyReadMode,
            "Operating in local-only read mode on the POS host. LAN remote transport is disabled by default until an operator explicitly enables an explicit LAN bind.",
            readOnlyRefreshSafe: true,
            repairRequired: false,
            denialCode: null,
            denialMessage: null);
    }

    public static RemoteReadOnlySessionResilienceResponse Classify(string? denialCode, string? denialMessage = null)
    {
        string state = ResolveState(denialCode, denialMessage);
        (string guidance, bool refreshSafe, bool repairRequired) = Describe(state);

        return Build(state, guidance, refreshSafe, repairRequired, denialCode, denialMessage);
    }

    private static string ResolveState(string? denialCode, string? denialMessage)
    {
        string code = (denialCode ?? string.Empty).Trim();

        string? byCode = code switch
        {
            RemoteReadOnlyBridgeDiagnostics.HandshakeExpiredOrInvalid => HandshakeExpired,
            RemoteReadOnlyBridgeDiagnostics.HandshakeMissing => HandshakeMissing,
            RemoteReadOnlyBridgeDiagnostics.HandshakeHeaderMissing => HandshakeMissing,
            RemoteReadOnlyBridgeDiagnostics.TerminalPending => TerminalPending,
            RemoteReadOnlyBridgeDiagnostics.TerminalDisabled => TerminalDisabled,
            RemoteReadOnlyBridgeDiagnostics.TerminalRevoked => TerminalRevoked,
            RemoteReadOnlyBridgeDiagnostics.TerminalUnknown => TerminalUnknown,
            RemoteReadOnlyBridgeDiagnostics.TerminalHeaderMissing => TerminalUnknown,
            RemoteReadOnlyBridgeDiagnostics.RemoteTransportDisabled => RemoteDisabled,
            RemoteReadOnlyBridgeDiagnostics.CookieTrustRejectedForRemoteLan => CookieRejected,
            _ => null
        };

        if (byCode is not null)
        {
            return byCode;
        }

        string haystack = (code + " " + (denialMessage ?? string.Empty)).ToLowerInvariant();

        if (haystack.Contains("expired", StringComparison.Ordinal)
            || haystack.Contains("sessioninvalid", StringComparison.Ordinal))
        {
            return HandshakeExpired;
        }

        if (haystack.Contains("revoked", StringComparison.Ordinal))
        {
            return TerminalRevoked;
        }

        if (haystack.Contains("pending", StringComparison.Ordinal))
        {
            return TerminalPending;
        }

        if (haystack.Contains("disabled by default", StringComparison.Ordinal)
            || haystack.Contains("remote disabled", StringComparison.Ordinal)
            || haystack.Contains("transport is disabled", StringComparison.Ordinal))
        {
            return RemoteDisabled;
        }

        if (haystack.Contains("disabled", StringComparison.Ordinal))
        {
            return TerminalDisabled;
        }

        if (haystack.Contains("cookie", StringComparison.Ordinal))
        {
            return CookieRejected;
        }

        if (haystack.Contains("unknown", StringComparison.Ordinal))
        {
            return TerminalUnknown;
        }

        if (haystack.Contains("handshake", StringComparison.Ordinal))
        {
            return HandshakeMissing;
        }

        return Unknown;
    }

    private static (string Guidance, bool ReadOnlyRefreshSafe, bool RepairRequired) Describe(string state)
    {
        return state switch
        {
            SessionActive => (
                "Read-only session is active. Displayed values reflect the last successful safe read.",
                true,
                false),
            LocalOnlyReadMode => (
                "Operating in local-only read mode. LAN remote transport remains disabled by default.",
                true,
                false),
            HandshakeExpired => (
                "The remote handshake session expired. Refresh read-only state to retry a safe read. If it keeps failing, re-pair this terminal manually on the POS host. The client never re-pairs on its own.",
                true,
                true),
            HandshakeMissing => (
                "No valid handshake session was presented. Re-pair this terminal manually on the POS host, then refresh. The client never re-pairs or discovers hosts on its own.",
                true,
                true),
            TerminalPending => (
                "This terminal is pending pairing approval on the POS host. Ask the operator to approve pairing, then refresh read-only state.",
                true,
                true),
            TerminalDisabled => (
                "This terminal was disabled by the operator. Read-only access is blocked until the operator re-enables it on the POS host.",
                false,
                false),
            TerminalRevoked => (
                "This terminal was revoked by the operator. Read-only access is blocked. Request a new pairing on the POS host.",
                false,
                true),
            TerminalUnknown => (
                "This terminal identity is not recognized. Re-pair this terminal manually on the POS host. The client never discovers hosts on its own.",
                false,
                true),
            RemoteDisabled => (
                "Remote read-only transport is disabled on the POS host. The operator must explicitly enable it for a specific LAN address before remote reads are possible.",
                false,
                false),
            CookieRejected => (
                "Operator session cookies are not accepted as remote LAN trust. Present terminal identity and handshake proof from a paired terminal.",
                false,
                true),
            _ => (
                "The remote read-only session state could not be classified. Refresh read-only state; if the problem persists, check pairing on the POS host.",
                true,
                false)
        };
    }

    private static RemoteReadOnlySessionResilienceResponse Build(
        string state,
        string guidance,
        bool readOnlyRefreshSafe,
        bool repairRequired,
        string? denialCode,
        string? denialMessage)
    {
        return new RemoteReadOnlySessionResilienceResponse(
            LifecycleState: state,
            Guidance: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(guidance) ?? string.Empty,
            ReadOnlyRefreshSafe: readOnlyRefreshSafe,
            RepairRequired: repairRequired,
            AutomaticRepairDisabled: true,
            StaleDataWarning: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(StaleDataWarning) ?? string.Empty,
            DenialCode: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(denialCode),
            DenialMessage: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(denialMessage));
    }
}
