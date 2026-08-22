using System.Text.Json;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;

internal static class LanM15RemoteReadOnlyLanSessionResilienceSmokeChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M15-D REMOTE READ-ONLY SESSION RESILIENCE SMOKE CHECKS =====");

        RequireActiveAndLocalOnly();
        RequireExpiredHandshakeGuidance();
        RequireTerminalDisabledAndRevokedGuidance();
        RequireRemoteDisabledGuidance();
        RequireNoAutomaticRepairOrDiscovery();
        RequirePayloadRedaction();

        Console.WriteLine("LAN-M15-D remote read-only session resilience smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireActiveAndLocalOnly()
    {
        RemoteReadOnlySessionResilienceResponse active = RemoteReadOnlySessionLifecycle.Active();
        Require(active.LifecycleState == RemoteReadOnlySessionLifecycle.SessionActive, "Active() must report SessionActive.");
        Require(active.ReadOnlyRefreshSafe, "Active session must be refresh-safe.");
        Require(active.AutomaticRepairDisabled, "Automatic re-pair must always be disabled.");

        RemoteReadOnlySessionResilienceResponse local = RemoteReadOnlySessionLifecycle.LocalOnly();
        Require(local.LifecycleState == RemoteReadOnlySessionLifecycle.LocalOnlyReadMode, "LocalOnly() must report LocalOnlyReadMode.");
        Require(local.AutomaticRepairDisabled, "Local-only session must keep automatic re-pair disabled.");
    }

    private static void RequireExpiredHandshakeGuidance()
    {
        RemoteReadOnlySessionResilienceResponse expired = RemoteReadOnlySessionLifecycle.Classify(
            RemoteReadOnlyBridgeDiagnostics.HandshakeExpiredOrInvalid,
            "Remote handshake session is invalid or expired.");

        Require(expired.LifecycleState == RemoteReadOnlySessionLifecycle.HandshakeExpired, "Expired handshake code must classify as HandshakeExpired.");
        Require(expired.ReadOnlyRefreshSafe, "Expired handshake must allow a manual read-only refresh retry.");
        Require(expired.RepairRequired, "Expired handshake must indicate re-pair may be required.");
        Require(expired.Guidance.Length > 0, "Expired handshake guidance must be present.");
    }

    private static void RequireTerminalDisabledAndRevokedGuidance()
    {
        RemoteReadOnlySessionResilienceResponse disabled = RemoteReadOnlySessionLifecycle.Classify(
            RemoteReadOnlyBridgeDiagnostics.TerminalDisabled);
        Require(disabled.LifecycleState == RemoteReadOnlySessionLifecycle.TerminalDisabled, "Disabled terminal code must classify as TerminalDisabled.");
        Require(!disabled.ReadOnlyRefreshSafe, "A disabled terminal must not be refresh-safe until the operator re-enables it.");

        RemoteReadOnlySessionResilienceResponse revoked = RemoteReadOnlySessionLifecycle.Classify(
            RemoteReadOnlyBridgeDiagnostics.TerminalRevoked);
        Require(revoked.LifecycleState == RemoteReadOnlySessionLifecycle.TerminalRevoked, "Revoked terminal code must classify as TerminalRevoked.");
        Require(revoked.RepairRequired, "A revoked terminal must require a new pairing on the POS host.");
    }

    private static void RequireRemoteDisabledGuidance()
    {
        RemoteReadOnlySessionResilienceResponse remoteDisabled = RemoteReadOnlySessionLifecycle.Classify(
            RemoteReadOnlyBridgeDiagnostics.RemoteTransportDisabled);
        Require(remoteDisabled.LifecycleState == RemoteReadOnlySessionLifecycle.RemoteDisabled, "Remote transport disabled code must classify as RemoteDisabled.");
        Require(!remoteDisabled.ReadOnlyRefreshSafe, "Remote-disabled state must not be refresh-safe.");

        RemoteReadOnlySessionResilienceResponse unknown = RemoteReadOnlySessionLifecycle.Classify(null, null);
        Require(unknown.LifecycleState == RemoteReadOnlySessionLifecycle.Unknown, "A null denial must classify as Unknown.");
        Require(unknown.AutomaticRepairDisabled, "Unknown state must keep automatic re-pair disabled.");
    }

    private static void RequireNoAutomaticRepairOrDiscovery()
    {
        string[] codes =
        {
            RemoteReadOnlyBridgeDiagnostics.HandshakeExpiredOrInvalid,
            RemoteReadOnlyBridgeDiagnostics.HandshakeMissing,
            RemoteReadOnlyBridgeDiagnostics.TerminalPending,
            RemoteReadOnlyBridgeDiagnostics.TerminalDisabled,
            RemoteReadOnlyBridgeDiagnostics.TerminalRevoked,
            RemoteReadOnlyBridgeDiagnostics.TerminalUnknown,
            RemoteReadOnlyBridgeDiagnostics.RemoteTransportDisabled,
            RemoteReadOnlyBridgeDiagnostics.CookieTrustRejectedForRemoteLan
        };

        foreach (string code in codes)
        {
            RemoteReadOnlySessionResilienceResponse resilience = RemoteReadOnlySessionLifecycle.Classify(code);
            string guidance = resilience.Guidance.ToLowerInvariant();

            Require(resilience.AutomaticRepairDisabled, $"Automatic re-pair must be disabled for {code}.");
            Require(!guidance.Contains("qr", StringComparison.Ordinal), $"Guidance for {code} must not mention QR.");
            Require(!guidance.Contains("scan", StringComparison.Ordinal), $"Guidance for {code} must not mention network scan.");
            Require(!guidance.Contains("automatic re-pair", StringComparison.Ordinal), $"Guidance for {code} must not promise automatic re-pair.");
            Require(!guidance.Contains("automatically", StringComparison.Ordinal), $"Guidance for {code} must not promise automatic actions.");
        }
    }

    private static void RequirePayloadRedaction()
    {
        RemoteReadOnlySessionResilienceResponse hostile = RemoteReadOnlySessionLifecycle.Classify(
            "RemoteHandshake.SessionInvalid",
            "token=supersecret salryn.sid=abc Data Source=C:\\data\\store.db");

        string payload = JsonSerializer.Serialize(hostile, JsonOptions);

        Require(!payload.Contains("supersecret", StringComparison.OrdinalIgnoreCase), "Session resilience payload must not leak tokens.");
        Require(!payload.Contains("salryn.sid", StringComparison.OrdinalIgnoreCase), "Session resilience payload must not leak session cookie names.");
        Require(!payload.Contains("Data Source=", StringComparison.OrdinalIgnoreCase), "Session resilience payload must not leak connection strings.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
