using System.Net;
using Microsoft.AspNetCore.Http;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Host;

internal static class LanM15RemoteReadOnlyLanFinalAcceptanceSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M15-F REMOTE READ-ONLY LAN FINAL ACCEPTANCE SMOKE CHECKS =====");

        RequireDisabledByDefault();
        RequireForbiddenBindRejected();
        RequireTerminalIdentityAndCookieBoundary();
        RequireSessionLifecycleSafe();
        RequireNegativePathHardening();

        Console.WriteLine("LAN-M15-F remote read-only LAN final acceptance smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireDisabledByDefault()
    {
        LanRemoteTransportConfig defaultConfig = LanRemoteTransportConfig.CreateDefault();
        Require(!defaultConfig.RemoteTransportEnabled, "Remote transport must be disabled by default.");

        RemoteReadOnlyPreflightResponse preflight =
            RemoteReadOnlyLanPreflightEvaluator.Evaluate(defaultConfig, auditPolicyAvailable: true);
        Require(preflight.OverallReadiness == RemoteReadOnlyLanPreflightEvaluator.ReadinessDisabled, "Default preflight must report Disabled readiness.");
        Require(preflight.ReadOnlyOnly, "Preflight must remain read-only only.");

        RemoteReadOnlyBindPolicyResponse bindPolicy = RemoteReadOnlyLanBindPolicyMapper.Map(defaultConfig);
        Require(bindPolicy.DisabledByDefault, "Bind policy must report disabled by default.");
        Require(bindPolicy.NoWildcardBind, "Bind policy must forbid wildcard bind.");
        Require(bindPolicy.SpecificIpOnly, "Bind policy must require a specific IP.");
    }

    private static void RequireForbiddenBindRejected()
    {
        LanRemoteTransportConfig forbidden = new(
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
            RemoteReadOnlyLanPreflightEvaluator.Evaluate(forbidden, auditPolicyAvailable: true);
        Require(preflight.OverallReadiness == RemoteReadOnlyLanPreflightEvaluator.ReadinessMisconfigured, "Forbidden bind must report Misconfigured readiness.");
        Require(SalrynHostCommandLoopbackRules.IsForbiddenBindAddress("0.0." + "0.0"), "Wildcard bind must be classified forbidden.");
    }

    private static void RequireTerminalIdentityAndCookieBoundary()
    {
        RemoteReadOnlyDiagnosticsResponse diagnostics =
            RemoteReadOnlyBridgeDiagnostics.BuildOperatorDiagnostics(LanRemoteTransportConfig.CreateDefault(), auditPolicyAvailable: true);
        Require(diagnostics.TerminalIdentityRequired, "Diagnostics must require terminal identity.");
        Require(diagnostics.HandshakeProofRequired, "Diagnostics must require handshake proof.");
        Require(!diagnostics.CookieTrustAcceptedForRemoteLan, "Cookie trust must not be accepted for remote LAN.");
        Require(!diagnostics.CorsEnabled, "CORS must remain disabled.");

        DefaultHttpContext remoteCookie = new();
        remoteCookie.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        remoteCookie.Request.Headers.Cookie = "salryn.sid=operator-only";
        RemoteReadOnlyBridgeRequestContext context = RemoteReadOnlyBridgeRequestContext.FromHttpContext(remoteCookie);
        (string code, _, _) = RemoteReadOnlyBridgeDiagnostics.ResolveBridgeDenial(context);
        Require(code == RemoteReadOnlyBridgeDiagnostics.CookieTrustRejectedForRemoteLan, "A remote cookie-only request must be denied as cookie trust rejected.");
    }

    private static void RequireSessionLifecycleSafe()
    {
        RemoteReadOnlySessionResilienceResponse revoked =
            RemoteReadOnlySessionLifecycle.Classify(RemoteReadOnlyBridgeDiagnostics.TerminalRevoked);
        Require(revoked.AutomaticRepairDisabled, "Lifecycle mapping must keep automatic re-pair disabled.");
        Require(!revoked.ReadOnlyRefreshSafe, "A revoked terminal must not be refresh-safe.");
        Require(revoked.Guidance.Length > 0, "Lifecycle guidance must be present.");
    }

    private static void RequireNegativePathHardening()
    {
        Require(!RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(new string('x', RemoteReadOnlyAbuseGuard.MaxHeaderLength + 1)), "Oversized headers must be rejected.");
        Require(!RemoteReadOnlyAbuseGuard.IsAcceptableCategory("checkout"), "Checkout must never be an allowed read category.");
        Require(RemoteReadOnlyAbuseGuard.SanitizeHostileText("token=abc") == "[redacted]", "Hostile secret markers must be redacted.");

        RemoteReadOnlyAbuseGuard.Reset();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string key = RemoteReadOnlyAbuseGuard.BuildDenialKey(Guid.NewGuid(), "192.168.1.51");
        SoftThrottleDecision decision = new(0, false);
        for (int attempt = 0; attempt < RemoteReadOnlyAbuseGuard.SoftThrottleThreshold; attempt++)
        {
            decision = RemoteReadOnlyAbuseGuard.RegisterDenial(key, now);
        }

        Require(decision.IsSoftThrottled, "Repeated denials must flag soft-throttle.");
        RemoteReadOnlyAbuseGuard.Reset();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
