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

        if (bridgeRequest.TerminalId == Guid.Empty || bridgeRequest.HandshakeSessionId == Guid.Empty)
        {
            await lanServices.ReadAttemptAudit
                .RecordDeniedAsync(
                    requestedOperation,
                    requestedCategory,
                    bridgeRequest.TerminalId,
                    bridgeRequest.OriginAddress,
                    "missing handshake",
                    cancellationToken)
                .ConfigureAwait(false);

            return (BridgeRejected(
                "RemoteReadOnlyBridge.HandshakeRequired",
                "Remote bridge requests require terminal identity and handshake session headers."), null);
        }

        Result<LanRemoteTransportConfig> configResult = await lanServices.Config
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configResult.IsFailure)
        {
            await lanServices.ReadAttemptAudit
                .RecordDeniedAsync(
                    requestedOperation,
                    requestedCategory,
                    bridgeRequest.TerminalId,
                    bridgeRequest.OriginAddress,
                    "remote disabled",
                    cancellationToken)
                .ConfigureAwait(false);

            return (BridgeRejected(configResult.ErrorDetail.Code, configResult.ErrorDetail.Message), null);
        }

        if (!configResult.Value.RemoteTransportEnabled)
        {
            await lanServices.ReadAttemptAudit
                .RecordDeniedAsync(
                    requestedOperation,
                    requestedCategory,
                    bridgeRequest.TerminalId,
                    bridgeRequest.OriginAddress,
                    "remote disabled",
                    cancellationToken)
                .ConfigureAwait(false);

            return (BridgeRejected(
                "RemoteReadOnlyBridge.RemoteDisabled",
                "Remote read-only bridge is disabled."), null);
        }

        Result<DeploymentIdentitySnapshot?> identityResult = await lanServices.DeploymentIdentity
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (identityResult.IsFailure || identityResult.Value is null || !identityResult.Value.IsConfigured)
        {
            await lanServices.ReadAttemptAudit
                .RecordDeniedAsync(
                    requestedOperation,
                    requestedCategory,
                    bridgeRequest.TerminalId,
                    bridgeRequest.OriginAddress,
                    "deployment identity missing",
                    cancellationToken)
                .ConfigureAwait(false);

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
            await lanServices.ReadAttemptAudit
                .RecordDeniedAsync(
                    requestedOperation,
                    requestedCategory,
                    bridgeRequest.TerminalId,
                    bridgeRequest.OriginAddress,
                    handshakeValidation.ErrorDetail.Code,
                    cancellationToken)
                .ConfigureAwait(false);

            return (BridgeRejected(
                handshakeValidation.ErrorDetail.Code,
                handshakeValidation.ErrorDetail.Message), null);
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
        return Results.Json(
            new
            {
                error = message,
                code
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
