using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Services;

namespace Salryn.Api.Local.RemotePos;

/// <summary>
/// LAN-M16 per-request authorization for the remote POS command surface. It is deliberately
/// SEPARATE from the read-only bridge authorization (<see cref="RemoteReadOnlyApiAuthorization"/>)
/// so remote POS writes are never routed through the read-only allow-list. It still reuses the
/// safe helpers: terminal/handshake extraction, oversized-header rejection, and sanitized denials.
/// Cookie-only trust is never accepted; only header-bound terminal + handshake + operator session.
/// </summary>
public static class RemotePosApiAuthorization
{
    public static RemotePosTrustContext BuildTrustContext(HttpContext httpContext)
    {
        RemoteReadOnlyBridgeRequestContext bridge = RemoteReadOnlyBridgeRequestContext.FromHttpContext(httpContext);

        // LAN-M16-QA-A: bind/validate on the server-observed origin (bridge.ObservedOrigin), never
        // the spoofable client-supplied bridge.OriginAddress header.
        return new RemotePosTrustContext(bridge.TerminalId, bridge.HandshakeSessionId, bridge.ObservedOrigin);
    }

    /// <summary>
    /// Rejects requests that are not header-bound remote bridge requests (e.g. a plain loopback
    /// browser request), oversized/malformed header material. Remote POS requires terminal identity.
    /// </summary>
    public static IResult? RequireRemoteBridge(HttpContext httpContext)
    {
        RemoteReadOnlyBridgeRequestContext bridge = RemoteReadOnlyBridgeRequestContext.FromHttpContext(httpContext);

        if (bridge.HasOversizedHeader)
        {
            return Rejected(
                "RemotePos.OversizedHeaderRejected",
                "Remote POS request headers exceed the safe size limit.",
                StatusCodes.Status403Forbidden);
        }

        if (!bridge.IsRemoteBridgeRequest || !bridge.HasTerminalHeader || !bridge.HasHandshakeHeader)
        {
            return Rejected(
                "RemotePos.TerminalHandshakeRequired",
                "Remote POS requires terminal identity and handshake proof headers. Cookie-only trust is not accepted.",
                StatusCodes.Status403Forbidden);
        }

        if (bridge.HasMalformedTerminalId || bridge.HasMalformedHandshakeId
            || bridge.TerminalId == Guid.Empty || bridge.HandshakeSessionId == Guid.Empty)
        {
            return Rejected(
                "RemotePos.TerminalHandshakeMalformed",
                "Remote POS terminal id / handshake session id are missing or malformed.",
                StatusCodes.Status403Forbidden);
        }

        return null;
    }

    public static string? ReadSessionToken(HttpContext httpContext) =>
        httpContext.Request.Headers[RemotePosHeaders.OperatorSession].FirstOrDefault();

    /// <summary>
    /// Full command authorization: header-bound remote bridge, host-issued operator session
    /// validation (trust re-checked every call), then the role/permission matrix decision.
    /// </summary>
    public static async Task<(IResult? Rejected, RemotePosOperatorSession? Session)> AuthorizeCommandAsync(
        HttpContext httpContext,
        RemotePosApiServices services,
        string command,
        CancellationToken cancellationToken = default)
    {
        if (RequireRemoteBridge(httpContext) is IResult bridgeRejected)
        {
            return (bridgeRejected, null);
        }

        RemotePosTrustContext trust = BuildTrustContext(httpContext);
        string? token = ReadSessionToken(httpContext);

        RemotePosSessionValidation validation = await services.SessionService
            .ValidateAsync(trust, token, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.Ok || validation.Session is null)
        {
            return (Rejected(validation.Code, validation.Message, StatusForSessionState(validation.State), validation.State), null);
        }

        RemotePosCommandAuthorization decision =
            RemotePosAuthorizationMatrix.Evaluate(command, validation.Session.Permissions);

        if (decision.Decision != RemotePosAuthorizationDecision.Allowed)
        {
            return (Rejected(decision.Code, decision.Message, StatusForDecision(decision.Decision)), null);
        }

        return (null, validation.Session);
    }

    public static IResult Rejected(string code, string message, int statusCode, string? state = null)
    {
        string safeCode = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(code) ?? "RemotePos.Denied";
        string safeMessage = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(message)
            ?? "Remote POS request was denied.";
        string? safeState = state is null ? null : RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(state);

        return Results.Json(
            new
            {
                error = safeMessage,
                code = safeCode,
                state = safeState,
                diagnostic = new
                {
                    code = safeCode,
                    category = "remote-pos-authorization",
                    message = safeMessage
                }
            },
            statusCode: statusCode);
    }

    private static int StatusForSessionState(string state) => state switch
    {
        RemotePosSessionState.SessionInvalid => StatusCodes.Status401Unauthorized,
        RemotePosSessionState.SessionExpired => StatusCodes.Status401Unauthorized,
        RemotePosSessionState.SessionRevoked => StatusCodes.Status401Unauthorized,
        RemotePosSessionState.UserInactive => StatusCodes.Status401Unauthorized,
        RemotePosSessionState.IdentityMismatch => StatusCodes.Status403Forbidden,
        RemotePosSessionState.OriginMismatch => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status403Forbidden,
    };

    private static int StatusForDecision(RemotePosAuthorizationDecision decision) => decision switch
    {
        RemotePosAuthorizationDecision.PermissionDenied => StatusCodes.Status403Forbidden,
        RemotePosAuthorizationDecision.NotSupported => StatusCodes.Status400BadRequest,
        RemotePosAuthorizationDecision.UnknownCommand => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status403Forbidden,
    };
}
