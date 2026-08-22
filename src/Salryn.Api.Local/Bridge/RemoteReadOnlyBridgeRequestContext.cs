using System.Net;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Core.RemoteClients;

namespace Salryn.Api.Local.Bridge;

public sealed class RemoteReadOnlyBridgeRequestContext
{
    public Guid TerminalId { get; }

    public Guid HandshakeSessionId { get; }

    public string OriginAddress { get; }

    // LAN-M16-QA-A: the server-observed connection origin (canonicalized). Unlike OriginAddress,
    // this is NEVER derived from the client-supplied X-Salryn-Remote-Origin header, so it cannot be
    // spoofed by the remote client. It is the trusted origin the remote POS operator session binds
    // to at login and re-checks on every command.
    public string ObservedOrigin { get; }

    public bool IsRemoteBridgeRequest { get; }

    public bool HasTerminalHeader { get; }

    public bool HasHandshakeHeader { get; }

    public bool IsLoopbackRequest { get; }

    public bool HasOperatorSessionCookie { get; }

    // LAN-M15-E: negative-path classification for oversized / malformed request material.
    public bool HasOversizedHeader { get; }

    public bool HasMalformedTerminalId { get; }

    public bool HasMalformedHandshakeId { get; }

    private RemoteReadOnlyBridgeRequestContext(
        Guid terminalId,
        Guid handshakeSessionId,
        string originAddress,
        string observedOrigin,
        bool isRemoteBridgeRequest,
        bool hasTerminalHeader,
        bool hasHandshakeHeader,
        bool isLoopbackRequest,
        bool hasOperatorSessionCookie,
        bool hasOversizedHeader,
        bool hasMalformedTerminalId,
        bool hasMalformedHandshakeId)
    {
        TerminalId = terminalId;
        HandshakeSessionId = handshakeSessionId;
        OriginAddress = originAddress;
        ObservedOrigin = observedOrigin;
        IsRemoteBridgeRequest = isRemoteBridgeRequest;
        HasTerminalHeader = hasTerminalHeader;
        HasHandshakeHeader = hasHandshakeHeader;
        IsLoopbackRequest = isLoopbackRequest;
        HasOperatorSessionCookie = hasOperatorSessionCookie;
        HasOversizedHeader = hasOversizedHeader;
        HasMalformedTerminalId = hasMalformedTerminalId;
        HasMalformedHandshakeId = hasMalformedHandshakeId;
    }

    public static RemoteReadOnlyBridgeRequestContext FromHttpContext(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string? terminalHeader = httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId].FirstOrDefault();
        string? sessionHeader = httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.HandshakeSessionId].FirstOrDefault();
        string? originHeader = httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.RemoteOrigin].FirstOrDefault();

        bool hasTerminalHeader = !string.IsNullOrWhiteSpace(terminalHeader);
        bool hasSessionHeader = !string.IsNullOrWhiteSpace(sessionHeader);
        IPAddress? remoteIpAddress = httpContext.Connection.RemoteIpAddress;
        bool isLoopbackRequest = remoteIpAddress is null || IPAddress.IsLoopback(remoteIpAddress);
        bool isRemoteBridgeRequest = hasTerminalHeader || hasSessionHeader || !isLoopbackRequest;

        bool hasOversizedHeader =
            !RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(terminalHeader)
            || !RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(sessionHeader)
            || !RemoteReadOnlyAbuseGuard.IsHeaderValueWithinLimit(originHeader);

        Guid terminalId = Guid.Empty;
        bool hasMalformedTerminalId = hasTerminalHeader && !Guid.TryParse(terminalHeader, out terminalId);
        if (hasMalformedTerminalId)
        {
            terminalId = Guid.Empty;
        }

        Guid handshakeSessionId = Guid.Empty;
        bool hasMalformedHandshakeId = hasSessionHeader && !Guid.TryParse(sessionHeader, out handshakeSessionId);
        if (hasMalformedHandshakeId)
        {
            handshakeSessionId = Guid.Empty;
        }

        string originAddress = ResolveOriginAddress(httpContext, originHeader);
        string observedOrigin = ResolveObservedOrigin(httpContext);
        bool hasOperatorSessionCookie = httpContext.Request.Cookies.ContainsKey(LocalApiSessionService.SessionCookieName);

        return new RemoteReadOnlyBridgeRequestContext(
            terminalId,
            handshakeSessionId,
            originAddress,
            observedOrigin,
            isRemoteBridgeRequest,
            hasTerminalHeader,
            hasSessionHeader,
            isLoopbackRequest,
            hasOperatorSessionCookie,
            hasOversizedHeader,
            hasMalformedTerminalId,
            hasMalformedHandshakeId);
    }

    public RemoteReadOnlyClientContext ToRemoteReadOnlyClientContext(
        Guid storeId,
        Guid hostId)
    {
        return new RemoteReadOnlyClientContext(
            TerminalId,
            storeId,
            hostId,
            HandshakeSessionId,
            OriginAddress,
            DateTimeOffset.UtcNow,
            ClientMachineName: null,
            ClientFingerprint: null);
    }

    private static string ResolveOriginAddress(HttpContext httpContext, string? originHeader)
    {
        if (!string.IsNullOrWhiteSpace(originHeader))
        {
            // LAN-M15-E: sanitize any operator/attacker-supplied origin header before it is
            // propagated into audit or the client context. Hostile control characters and
            // secret markers are stripped/redacted here.
            return RemoteReadOnlyAbuseGuard.SanitizeHostileText(originHeader);
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    // LAN-M16-QA-A: resolve the trusted, server-observed connection origin. This deliberately
    // ignores every client-supplied header (including X-Salryn-Remote-Origin) so a remote client
    // cannot spoof the origin the remote POS session is bound to. Loopback (IPv4/IPv6 and the
    // null "in-process" case) canonicalizes to a single stable token so a local operator's session
    // is never falsely rejected for an origin mismatch, while distinct LAN peers get distinct
    // observed origins. IPv4-mapped IPv6 addresses are normalized to their IPv4 form for a stable
    // comparison key.
    private static string ResolveObservedOrigin(HttpContext httpContext)
    {
        IPAddress? remoteIpAddress = httpContext.Connection.RemoteIpAddress;

        if (remoteIpAddress is null || IPAddress.IsLoopback(remoteIpAddress))
        {
            return LoopbackObservedOrigin;
        }

        if (remoteIpAddress.IsIPv4MappedToIPv6)
        {
            remoteIpAddress = remoteIpAddress.MapToIPv4();
        }

        return remoteIpAddress.ToString();
    }

    // Stable, non-secret canonical token for a local/loopback connection origin.
    public const string LoopbackObservedOrigin = "loopback";
}
