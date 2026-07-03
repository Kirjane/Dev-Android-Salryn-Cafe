using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.RemoteClients;

namespace Salryn.Api.Local.Services;

public static class RemoteReadOnlyApiAuthorization
{
    public static RemoteReadOnlyBridgeRequestContext TryGetRemoteBridgeContext(HttpContext httpContext) =>
        RemoteReadOnlyBridgeRequestContext.FromHttpContext(httpContext);

    public static async Task<(IResult? Rejected, RemoteReadOnlyClientContext? BridgeContext)> AuthorizeBridgeAsync(
        RemoteReadOnlyBridgeRequestContext bridgeRequest,
        LanRemoteTransportApiServices lanServices,
        string requestedOperation,
        string requestedCategory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bridgeRequest);
        ArgumentNullException.ThrowIfNull(lanServices);

        // LAN-M15-E: repeated denials are counted and flagged in memory. The soft-throttle
        // never hard-locks a terminal; the authorization decision below stays fail-closed.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string denialKey = RemoteReadOnlyAbuseGuard.BuildDenialKey(bridgeRequest.TerminalId, bridgeRequest.OriginAddress);

        async Task RecordDeniedAsync(string reason)
        {
            SoftThrottleDecision throttle = RemoteReadOnlyAbuseGuard.RegisterDenial(denialKey, now);
            string safeReason = throttle.IsSoftThrottled ? reason + " (soft-throttled)" : reason;

            await lanServices.ReadAttemptAudit
                .RecordDeniedAsync(
                    requestedOperation,
                    requestedCategory,
                    bridgeRequest.TerminalId,
                    bridgeRequest.OriginAddress,
                    safeReason,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // LAN-M15-E: reject oversized request material before touching any service.
        if (bridgeRequest.HasOversizedHeader)
        {
            await RecordDeniedAsync("oversized header rejected").ConfigureAwait(false);

            return (BridgeRejected(
                "RemoteReadOnlyBridge.OversizedHeaderRejected",
                "Remote bridge request headers exceed the safe size limit."), null);
        }

        // LAN-M15-E: only allow-listed safe read categories may proceed.
        if (!RemoteReadOnlyAbuseGuard.IsAcceptableCategory(requestedCategory))
        {
            await RecordDeniedAsync("unsupported category rejected").ConfigureAwait(false);

            return (BridgeRejected(
                RemoteReadOnlyBridgeDiagnostics.UnsafeCategoryDenied,
                "Requested category is not an allowed read-only category."), null);
        }

        if (bridgeRequest.TerminalId == Guid.Empty || bridgeRequest.HandshakeSessionId == Guid.Empty)
        {
            (string denialCode, string denialMessage, string auditReason) =
                RemoteReadOnlyBridgeDiagnostics.ResolveBridgeDenial(bridgeRequest);

            await RecordDeniedAsync(auditReason).ConfigureAwait(false);

            return (BridgeRejected(denialCode, denialMessage), null);
        }

        Result<LanRemoteTransportConfig> configResult = await lanServices.Config
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configResult.IsFailure)
        {
            await RecordDeniedAsync("remote disabled").ConfigureAwait(false);

            return (BridgeRejected(configResult.ErrorDetail.Code, configResult.ErrorDetail.Message), null);
        }

        if (!configResult.Value.RemoteTransportEnabled)
        {
            await RecordDeniedAsync("remote disabled").ConfigureAwait(false);

            return (BridgeRejected(
                "RemoteReadOnlyBridge.RemoteDisabled",
                "Remote read-only bridge is disabled."), null);
        }

        Result<DeploymentIdentitySnapshot?> identityResult = await lanServices.DeploymentIdentity
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (identityResult.IsFailure || identityResult.Value is null || !identityResult.Value.IsConfigured)
        {
            await RecordDeniedAsync("deployment identity missing").ConfigureAwait(false);

            return (BridgeRejected(
                "RemoteReadOnlyBridge.DeploymentIdentityRequired",
                "Deployment identity must be configured before remote bridge reads."), null);
        }

        DeploymentIdentitySnapshot identity = identityResult.Value;

        Result handshakeValidation = await lanServices.Handshake
            .ValidateHandshakeSessionAsync(
                bridgeRequest.TerminalId,
                bridgeRequest.HandshakeSessionId,
                cancellationToken)
            .ConfigureAwait(false);

        if (handshakeValidation.IsFailure)
        {
            (string denialCode, string denialMessage, string auditReason) =
                RemoteReadOnlyBridgeDiagnostics.MapHandshakeFailure(handshakeValidation.ErrorDetail.Code);

            await RecordDeniedAsync(auditReason).ConfigureAwait(false);

            return (BridgeRejected(denialCode, denialMessage), null);
        }

        RemoteReadOnlyClientContext clientContext = bridgeRequest.ToRemoteReadOnlyClientContext(
            identity.Store.StoreId,
            identity.Host.HostId);

        await lanServices.ReadAttemptAudit
            .RecordAllowedAsync(
                requestedOperation,
                requestedCategory,
                bridgeRequest.TerminalId,
                bridgeRequest.OriginAddress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (null, clientContext);
    }

    private static IResult BridgeRejected(string code, string message)
    {
        string safeCode = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(code) ?? "RemoteReadOnlyBridge.Denied";
        string safeMessage = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(message)
            ?? "Remote bridge authorization failed.";

        return Results.Json(
            new
            {
                error = safeMessage,
                code = safeCode,
                diagnostic = new
                {
                    code = safeCode,
                    category = "authorization",
                    message = safeMessage
                }
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
