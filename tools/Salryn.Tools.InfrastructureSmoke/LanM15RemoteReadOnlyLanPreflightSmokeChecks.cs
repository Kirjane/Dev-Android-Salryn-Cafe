using System.Text.Json;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Host;

internal static class LanM15RemoteReadOnlyLanPreflightSmokeChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M15 REMOTE READ-ONLY LAN PREFLIGHT SMOKE CHECKS =====");

        RequireDisabledDefaultPreflight();
        RequireMisconfiguredForbiddenBindPreflight();
        RequireReadyExplicitBindPreflight();
        RequirePayloadRedaction();
        RequireDiagnosticsStillAvailable();

        Console.WriteLine("LAN-M15 remote read-only LAN preflight smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireDisabledDefaultPreflight()
    {
        LanRemoteTransportConfig disabled = LanRemoteTransportConfig.CreateDefault();
        RemoteReadOnlyPreflightResponse preflight =
            RemoteReadOnlyLanPreflightEvaluator.Evaluate(disabled, auditPolicyAvailable: true);

        Require(preflight.OverallReadiness == RemoteReadOnlyLanPreflightEvaluator.ReadinessDisabled, "Default config must report Disabled readiness.");
        Require(preflight.ReadOnlyOnly, "Preflight must remain read-only only.");
        Require(preflight.Checks.Any(check => check.Code == "RemoteTransportDisabledByDefault"), "Preflight must include disabled-by-default check.");
        Require(preflight.Checks.Any(check => check.Code == "CookieTrustRejectedForRemoteLan"), "Preflight must report cookie trust rejection.");
        Require(preflight.Checks.Any(check => check.Code == "MutationCheckoutAdminForbidden"), "Preflight must report mutation/checkout/admin forbidden.");
        Require(preflight.SafeNextSteps.Count > 0, "Preflight must include safe next steps.");
    }

    private static void RequireMisconfiguredForbiddenBindPreflight()
    {
        LanRemoteTransportConfig misconfigured = new(
            RemoteTransportEnabled: true,
            BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
            BindAddress: "0.0." + "0.0",
            Port: LanRemoteTransportConfig.DefaultPort,
            RequirePairedTerminal: true,
            RequireActiveTerminal: true,
            RejectUnknownTerminals: true,
            AllowOfficialCommandsRemotely: false,
            AllowAdminCommandsRemotely: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedByUserId: Guid.Parse("00000000-0000-0000-0000-000000000001"));

        RemoteReadOnlyPreflightResponse preflight =
            RemoteReadOnlyLanPreflightEvaluator.Evaluate(misconfigured, auditPolicyAvailable: false);

        Require(preflight.OverallReadiness == RemoteReadOnlyLanPreflightEvaluator.ReadinessMisconfigured, "Forbidden bind must report Misconfigured readiness.");
        Require(preflight.BlockedReasons.Count > 0, "Misconfigured preflight must include blocked reasons.");
        Require(preflight.Checks.Any(check => check.Code == "ConfiguredBindAddressAllowed" && check.Status == "Fail"), "Forbidden bind must fail configured bind check.");
    }

    private static void RequireReadyExplicitBindPreflight()
    {
        LanRemoteTransportConfig enabled = new(
            RemoteTransportEnabled: true,
            BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
            BindAddress: "192.168.1.10",
            Port: LanRemoteTransportConfig.DefaultPort,
            RequirePairedTerminal: true,
            RequireActiveTerminal: true,
            RejectUnknownTerminals: true,
            AllowOfficialCommandsRemotely: false,
            AllowAdminCommandsRemotely: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedByUserId: Guid.Parse("00000000-0000-0000-0000-000000000001"));

        RemoteReadOnlyPreflightResponse preflight =
            RemoteReadOnlyLanPreflightEvaluator.Evaluate(enabled, auditPolicyAvailable: true);

        Require(preflight.OverallReadiness == RemoteReadOnlyLanPreflightEvaluator.ReadinessReady, "Explicit valid LAN bind must report Ready readiness.");
        Require(preflight.Checks.Any(check => check.Code == "ExplicitLanBindConfigured" && check.Status == "Pass"), "Explicit LAN bind must pass configured check.");
        Require(preflight.Checks.Any(check => check.Code == "HandshakeProofRequired"), "Ready preflight must still require handshake proof.");
    }

    private static void RequirePayloadRedaction()
    {
        LanRemoteTransportConfig disabled = LanRemoteTransportConfig.CreateDefault();
        RemoteReadOnlyPreflightResponse preflight =
            RemoteReadOnlyLanPreflightEvaluator.Evaluate(disabled, auditPolicyAvailable: true);

        string payload = JsonSerializer.Serialize(preflight, JsonOptions);

        Require(!payload.Contains("salryn.sid", StringComparison.OrdinalIgnoreCase), "Preflight payload must not contain session cookie names.");
        Require(!payload.Contains("token=", StringComparison.OrdinalIgnoreCase), "Preflight payload must not contain raw tokens.");
        Require(!payload.Contains("password=", StringComparison.OrdinalIgnoreCase), "Preflight payload must not contain passwords.");
        Require(!payload.Contains("Data Source=", StringComparison.OrdinalIgnoreCase), "Preflight payload must not contain connection strings.");
        Require(!payload.Contains(".db", StringComparison.OrdinalIgnoreCase), "Preflight payload must not contain database file paths.");
        Require(RemoteReadOnlyLanPreflightEvaluator.PayloadContainsSecretMarker("token=secret password=secret credential=secret salryn.sid=abc Data Source=C:\\data\\store.db sqlite"), "Secret marker detection must identify unsafe strings.");
    }

    private static void RequireDiagnosticsStillAvailable()
    {
        LanRemoteTransportConfig disabled = LanRemoteTransportConfig.CreateDefault();
        RemoteReadOnlyDiagnosticsResponse diagnostics =
            RemoteReadOnlyBridgeDiagnostics.BuildOperatorDiagnostics(disabled, auditPolicyAvailable: true);

        Require(diagnostics.Items.Count > 0, "Diagnostics endpoint foundation must remain available.");
        Require(diagnostics.TerminalIdentityRequired, "Diagnostics must still require terminal identity.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
