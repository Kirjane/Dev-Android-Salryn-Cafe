using Salryn.Core.RemoteClients;

namespace Salryn.Api.Local.Bridge;

public sealed class RemoteReadOnlyBridgeRequestContext
{
    public Guid TerminalId { get; }

    public Guid HandshakeSessionId { get; }

    public string OriginAddress { get; }

    public bool IsRemoteBridgeRequest { get; }

    private RemoteReadOnlyBridgeRequestContext(
        Guid terminalId,
        Guid handshakeSessionId,
        string originAddress,
        bool isRemoteBridgeRequest)
    {
        TerminalId = terminalId;
        HandshakeSessionId = handshakeSessionId;
        OriginAddress = originAddress;
        IsRemoteBridgeRequest = isRemoteBridgeRequest;
    }

    public static RemoteReadOnlyBridgeRequestContext FromHttpContext(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string? terminalHeader = httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId].FirstOrDefault();
        string? sessionHeader = httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.HandshakeSessionId].FirstOrDefault();
        string? originHeader = httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.RemoteOrigin].FirstOrDefault();

        bool hasTerminalHeader = !string.IsNullOrWhiteSpace(terminalHeader);
        bool hasSessionHeader = !string.IsNullOrWhiteSpace(sessionHeader);
        bool isRemoteBridgeRequest = hasTerminalHeader || hasSessionHeader;

        Guid terminalId = Guid.Empty;
        if (hasTerminalHeader && !Guid.TryParse(terminalHeader, out terminalId))
        {
            terminalId = Guid.Empty;
        }

        Guid handshakeSessionId = Guid.Empty;
        if (hasSessionHeader && !Guid.TryParse(sessionHeader, out handshakeSessionId))
        {
            handshakeSessionId = Guid.Empty;
        }

        string originAddress = ResolveOriginAddress(httpContext, originHeader);

        return new RemoteReadOnlyBridgeRequestContext(
            terminalId,
            handshakeSessionId,
            originAddress,
            isRemoteBridgeRequest);
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
            return originHeader.Trim();
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}
