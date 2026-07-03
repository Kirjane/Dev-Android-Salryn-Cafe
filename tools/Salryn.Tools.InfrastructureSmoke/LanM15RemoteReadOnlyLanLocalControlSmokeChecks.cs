using System.Net;
using Microsoft.AspNetCore.Http;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Bridge;

internal static class LanM15RemoteReadOnlyLanLocalControlSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M15-C LOCAL-ONLY LAN CONTROL SMOKE CHECKS =====");

        RequireLoopbackOperatorAccepted();
        RequireNonLoopbackRejected();
        RequireBridgeHeaderRejected();
        RequireRemoteOriginHeaderRejected();

        Console.WriteLine("LAN-M15-C local-only LAN control smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireLoopbackOperatorAccepted()
    {
        DefaultHttpContext loopback = new();
        loopback.Connection.RemoteIpAddress = IPAddress.Loopback;

        Require(
            LocalLanControlAuthorization.IsLoopbackOperatorRequest(loopback),
            "Loopback request without remote bridge headers must be eligible for local operator control.");
    }

    private static void RequireNonLoopbackRejected()
    {
        DefaultHttpContext remote = new();
        remote.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.70");

        Require(
            !LocalLanControlAuthorization.IsLoopbackOperatorRequest(remote),
            "Non-loopback request must never be treated as a local operator control request.");

        DefaultHttpContext remoteWithCookie = new();
        remoteWithCookie.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.71");
        remoteWithCookie.Request.Headers.Cookie = $"{LocalApiSessionService.SessionCookieName}=operator-session-only";

        Require(
            !LocalLanControlAuthorization.IsLoopbackOperatorRequest(remoteWithCookie),
            "A remote request carrying an operator cookie must not gain local control access.");
    }

    private static void RequireBridgeHeaderRejected()
    {
        DefaultHttpContext loopbackWithTerminal = new();
        loopbackWithTerminal.Connection.RemoteIpAddress = IPAddress.Loopback;
        loopbackWithTerminal.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId] = Guid.NewGuid().ToString();

        Require(
            !LocalLanControlAuthorization.IsLoopbackOperatorRequest(loopbackWithTerminal),
            "A request carrying a remote terminal header must be rejected from local control even on loopback.");

        DefaultHttpContext loopbackWithHandshake = new();
        loopbackWithHandshake.Connection.RemoteIpAddress = IPAddress.Loopback;
        loopbackWithHandshake.Request.Headers[RemoteReadOnlyBridgeHeaders.HandshakeSessionId] = Guid.NewGuid().ToString();

        Require(
            !LocalLanControlAuthorization.IsLoopbackOperatorRequest(loopbackWithHandshake),
            "A request carrying a remote handshake header must be rejected from local control even on loopback.");
    }

    private static void RequireRemoteOriginHeaderRejected()
    {
        DefaultHttpContext loopbackWithOrigin = new();
        loopbackWithOrigin.Connection.RemoteIpAddress = IPAddress.Loopback;
        loopbackWithOrigin.Request.Headers[RemoteReadOnlyBridgeHeaders.RemoteOrigin] = "192.168.1.72";

        Require(
            !LocalLanControlAuthorization.IsLoopbackOperatorRequest(loopbackWithOrigin),
            "A request carrying a remote-origin header must be rejected from local control even on loopback.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
