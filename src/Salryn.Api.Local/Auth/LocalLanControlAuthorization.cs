using System.Net;
using Salryn.Api.Local.Bridge;

namespace Salryn.Api.Local.Auth;

/// <summary>
/// LAN-M15-C: fail-closed authorization for the local-only LAN control surface.
/// Local control (enable/disable transport, configure bind address, disable/revoke
/// terminals) is available only to the localhost operator session. Remote LAN bridge
/// clients (identified by terminal/handshake headers or a non-loopback origin) are
/// always rejected, and cookies alone never grant remote LAN control.
/// </summary>
public static class LocalLanControlAuthorization
{
    public static IResult? RequireLocalOperatorSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        if (!IsLoopbackOperatorRequest(httpContext))
        {
            return LocalOperatorRequired();
        }

        return LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions);
    }

    public static IResult? RequireLocalOperator(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission)
    {
        if (!IsLoopbackOperatorRequest(httpContext))
        {
            return LocalOperatorRequired();
        }

        return LocalApiPermissionGuards.RequirePermission(httpContext, sessions, permission);
    }

    public static bool IsLoopbackOperatorRequest(HttpContext httpContext)
    {
        IPAddress? remoteIp = httpContext.Connection.RemoteIpAddress;
        bool isLoopback = remoteIp is null || IPAddress.IsLoopback(remoteIp);

        bool carriesRemoteBridgeHeaders =
            httpContext.Request.Headers.ContainsKey(RemoteReadOnlyBridgeHeaders.TerminalId)
            || httpContext.Request.Headers.ContainsKey(RemoteReadOnlyBridgeHeaders.HandshakeSessionId)
            || httpContext.Request.Headers.ContainsKey(RemoteReadOnlyBridgeHeaders.RemoteOrigin);

        return isLoopback && !carriesRemoteBridgeHeaders;
    }

    public static LocalApiSession? GetLocalOperatorSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
        return sessions.GetRequiredSession(sessionId);
    }

    private static IResult LocalOperatorRequired()
    {
        return Results.Json(
            new
            {
                error = "Local LAN control is available only to the localhost operator on the POS host.",
                code = "LocalLanControl.LocalOperatorRequired"
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
