using System.Net;
using Microsoft.Extensions.Primitives;
using Salryn.Api.Local.Bridge;

namespace Salryn.Api.Local.Auth;

/// <summary>
/// Shared loopback-only boundary for the host-local operator console. Remote LAN clients may load
/// the static app shell, but they cannot use cookies or local API credentials as console trust.
/// Browser-mediated cross-site localhost requests are also rejected when standard browser origin
/// metadata identifies them as coming from outside localhost.
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

        return isLoopback &&
            !carriesRemoteBridgeHeaders &&
            !HasRejectedBrowserOrigin(httpContext);
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

    private static bool HasRejectedBrowserOrigin(HttpContext httpContext)
    {
        IHeaderDictionary headers = httpContext.Request.Headers;

        if (headers.TryGetValue("Sec-Fetch-Site", out StringValues fetchSiteValues) &&
            fetchSiteValues.Any(value => string.Equals(value, "cross-site", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (headers.TryGetValue("Origin", out StringValues originValues))
        {
            return originValues.Any(origin => !IsAllowedLocalOrigin(origin));
        }

        return false;
    }

    private static bool IsAllowedLocalOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host, out IPAddress? hostIp) && IPAddress.IsLoopback(hostIp));
    }
}
