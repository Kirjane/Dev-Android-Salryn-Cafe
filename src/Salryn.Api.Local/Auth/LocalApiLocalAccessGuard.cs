using System.Net;
using Salryn.Api.Local.Bridge;

namespace Salryn.Api.Local.Auth;

/// <summary>
/// Shared loopback-only boundary for the host-local operator console. Remote LAN clients may load
/// the static app shell, but they cannot use cookies or local API credentials as console trust.
/// </summary>
public static class LocalApiLocalAccessGuard
{
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

    public static IResult? RequireLoopbackOperatorRequest(HttpContext httpContext)
    {
        return IsLoopbackOperatorRequest(httpContext)
            ? null
            : LocalOperatorRequired();
    }

    public static IResult LocalOperatorRequired()
    {
        return Results.Json(
            new
            {
                error = "Host-local operator console access is available only from localhost on the POS host.",
                code = "LocalApi.LocalOperatorRequired"
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
