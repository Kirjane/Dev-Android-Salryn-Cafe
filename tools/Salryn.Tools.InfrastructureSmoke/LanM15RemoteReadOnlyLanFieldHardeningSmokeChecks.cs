using System.Net;
using Microsoft.AspNetCore.Http;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Host;

internal static class LanM15RemoteReadOnlyLanFieldHardeningSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M15 REMOTE READ-ONLY LAN FIELD HARDENING SMOKE CHECKS =====");

        RequireLoopbackClassification();
        RequireNonLoopbackBridgeClassification();
        RequireCookieTrustRejection();
        RequireMissingHeaderDenials();
        RequireOperatorDiagnosticsSafety();
        RequireSanitization();

        Console.WriteLine("LAN-M15 remote read-only LAN field hardening smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireLoopbackClassification()
    {
        DefaultHttpContext loopback = new();
        loopback.Connection.RemoteIpAddress = IPAddress.Loopback;

        RemoteReadOnlyBridgeRequestContext context =
            RemoteReadOnlyBridgeRequestContext.FromHttpContext(loopback);

        Require(!context.IsRemoteBridgeRequest, "Loopback requests without bridge headers must use localhost path.");
        Require(context.IsLoopbackRequest, "Loopback requests must be classified as loopback.");
    }

    private static void RequireNonLoopbackBridgeClassification()
    {
        DefaultHttpContext remote = new();
        remote.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");

        RemoteReadOnlyBridgeRequestContext context =
            RemoteReadOnlyBridgeRequestContext.FromHttpContext(remote);

        Require(context.IsRemoteBridgeRequest, "Non-loopback requests without headers must be bridge requests.");
        Require(!context.IsLoopbackRequest, "Non-loopback requests must not be classified as loopback.");
        Require(!context.HasTerminalHeader, "Missing terminal header must be visible to diagnostics.");
        Require(!context.HasHandshakeHeader, "Missing handshake header must be visible to diagnostics.");
    }

    private static void RequireCookieTrustRejection()
    {
        DefaultHttpContext remoteWithCookie = new();
        remoteWithCookie.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.51");
        remoteWithCookie.Request.Headers.Cookie = $"{LocalApiSessionService.SessionCookieName}=operator-session-only";

        RemoteReadOnlyBridgeRequestContext context =
            RemoteReadOnlyBridgeRequestContext.FromHttpContext(remoteWithCookie);

        (string code, string message, string auditReason) =
            RemoteReadOnlyBridgeDiagnostics.ResolveBridgeDenial(context);

        Require(context.HasOperatorSessionCookie, "Operator session cookie presence must be detected.");
        Require(code == RemoteReadOnlyBridgeDiagnostics.CookieTrustRejectedForRemoteLan, "Cookie-only non-loopback requests must be rejected as cookie trust.");
        Require(auditReason == "cookie trust rejected", "Cookie trust rejection must be audited safely.");
        Require(!message.Contains("operator-session-only", StringComparison.Ordinal), "Denied message must not echo cookie value.");
    }

    private static void RequireMissingHeaderDenials()
    {
        DefaultHttpContext remote = new();
        remote.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.52");

        RemoteReadOnlyBridgeRequestContext context =
            RemoteReadOnlyBridgeRequestContext.FromHttpContext(remote);

        (string terminalCode, _, string terminalAudit) =
            RemoteReadOnlyBridgeDiagnostics.ResolveBridgeDenial(context);

        Require(terminalCode == RemoteReadOnlyBridgeDiagnostics.TerminalHeaderMissing, "Non-loopback missing terminal header must map to terminal header missing.");
        Require(terminalAudit == "terminal header missing", "Terminal header missing must be audited safely.");

        DefaultHttpContext remoteTerminalOnly = new();
        remoteTerminalOnly.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.53");
        remoteTerminalOnly.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId] = Guid.NewGuid().ToString();

        RemoteReadOnlyBridgeRequestContext terminalOnly =
            RemoteReadOnlyBridgeRequestContext.FromHttpContext(remoteTerminalOnly);

        (string handshakeCode, _, string handshakeAudit) =
            RemoteReadOnlyBridgeDiagnostics.ResolveBridgeDenial(terminalOnly);

        Require(handshakeCode == RemoteReadOnlyBridgeDiagnostics.HandshakeHeaderMissing, "Non-loopback missing handshake header must map to handshake header missing.");
        Require(handshakeAudit == "handshake header missing", "Handshake header missing must be audited safely.");
    }

    private static void RequireOperatorDiagnosticsSafety()
    {
        LanRemoteTransportConfig disabled = LanRemoteTransportConfig.CreateDefault();
        RemoteReadOnlyDiagnosticsResponse diagnostics =
            RemoteReadOnlyBridgeDiagnostics.BuildOperatorDiagnostics(disabled, auditPolicyAvailable: true);

        Require(diagnostics.LanDisabledByDefault, "Diagnostics must report LAN disabled by default.");
        Require(diagnostics.TerminalIdentityRequired, "Diagnostics must require terminal identity.");
        Require(diagnostics.HandshakeProofRequired, "Diagnostics must require handshake proof.");
        Require(!diagnostics.CookieTrustAcceptedForRemoteLan, "Diagnostics must not accept cookie LAN trust.");
        Require(!diagnostics.CorsEnabled, "Diagnostics must report CORS disabled.");
        Require(diagnostics.ReadOnlyOnly, "Diagnostics must state read-only only.");
        Require(diagnostics.Items.Any(item => item.Code == RemoteReadOnlyBridgeDiagnostics.RemoteTransportDisabled), "Diagnostics must include remote transport disabled item.");
    }

    private static void RequireSanitization()
    {
        string sanitized = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText("token=secret salryn.sid=abc Data Source=C:\\data\\store.db")!;
        Require(sanitized == "[redacted]", "Sanitization must redact secret markers from denied responses.");

        (string code, string message, _) = RemoteReadOnlyBridgeDiagnostics.MapHandshakeFailure("RemoteHandshake.SessionInvalid");
        Require(code == RemoteReadOnlyBridgeDiagnostics.HandshakeExpiredOrInvalid, "Handshake invalid must map to safe diagnostic code.");
        Require(!message.Contains("sqlite", StringComparison.OrdinalIgnoreCase), "Handshake failure message must remain safe.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
