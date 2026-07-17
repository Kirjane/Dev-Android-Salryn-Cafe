using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.RemotePos;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.Services.Licensing;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Api.Local;

/// <summary>
/// LAN-M15-C: local-only LAN control endpoints. Every route is guarded by
/// <see cref="LocalLanControlAuthorization"/> so that only the localhost operator
/// session on the POS host can view or change LAN configuration and terminal trust.
/// Remote LAN bridge clients are rejected and cannot reach these controls. These
/// endpoints never allow catalog, checkout, official, backup, or settings mutation.
/// </summary>
public static class LocalLanControlEndpoints
{
    public static WebApplication MapLocalLanControlEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/lan-control");

        group.MapGet("/status", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperatorSession(httpContext, sessions);
            if (rejected is not null)
            {
                return rejected;
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            LocalLanControlStatusResponse status = await control
                .GetStatusAsync(actor, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(status);
        });

        group.MapGet("/runtime-status", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperatorSession(httpContext, sessions);
            if (rejected is not null)
            {
                return rejected;
            }

            LocalLanControlRuntimeStatusResponse status = await control
                .GetRuntimeStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(status);
        });

        group.MapPost("/remote-transport", async (
            LocalLanControlRemoteTransportRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.SettingsManage);
            if (rejected is not null)
            {
                return rejected;
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlStatusResponse> result = await control
                .SetRemoteTransportAsync(request.Enable, request.BindAddress, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapPost("/bind/repair", async (
            LocalLanControlBindRepairRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.SettingsManage);
            if (rejected is not null)
            {
                return rejected;
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlStatusResponse> result = await control
                .RepairConfiguredBindAsync(request.BindIp, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapPost("/remote-pos", async (
            LocalLanControlRemotePosRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.SettingsManage);
            if (rejected is not null)
            {
                return rejected;
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlStatusResponse> result = await control
                .SetRemotePosRuntimeAsync(request.Enable, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapGet("/remote-pos/pairing", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemotePosApiServices remotePos) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalView);
            if (rejected is not null)
            {
                return rejected;
            }

            return Results.Ok(remotePos.PairingGate.GetHostStatus());
        });

        group.MapPost("/remote-pos/pairing/join-code/rotate", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemotePosApiServices remotePos,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            return ToApiResult(remotePos.PairingGate.RotateJoinCode());
        });

        group.MapGet("/remote-pos/pairing/requests", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemotePosApiServices remotePos) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalView);
            if (rejected is not null)
            {
                return rejected;
            }

            return ToApiResult(remotePos.PairingGate.ListRequests());
        });

        group.MapPost("/remote-pos/pairing/requests/{requestId}/accept", async (
            string requestId,
            RemotePosApproveJoinRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemotePosApiServices remotePos,
            CancellationToken cancellationToken) =>
        {
            _ = request;
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            if (!Guid.TryParse(requestId, out Guid parsedRequestId))
            {
                return InvalidRequestId();
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<RemotePosApproveJoinResponse> result = await remotePos.PairingGate
                .AcceptAsync(parsedRequestId, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapPost("/remote-pos/pairing/requests/{requestId}/reject", (
            string requestId,
            RemotePosRejectJoinRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemotePosApiServices remotePos) =>
        {
            _ = request;
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            if (!Guid.TryParse(requestId, out Guid parsedRequestId))
            {
                return InvalidRequestId();
            }

            return ToApiResult(remotePos.PairingGate.Reject(parsedRequestId));
        });

        group.MapPost("/terminals/{terminalId}/disable", async (
            string terminalId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            if (!Guid.TryParse(terminalId, out Guid parsedTerminalId))
            {
                return InvalidTerminalId();
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlTerminalActionResponse> result = await control
                .DisableTerminalAsync(parsedTerminalId, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapPost("/terminals/{terminalId}/enable", async (
            string terminalId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            if (!Guid.TryParse(terminalId, out Guid parsedTerminalId))
            {
                return InvalidTerminalId();
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlTerminalActionResponse> result = await control
                .EnableTerminalAsync(parsedTerminalId, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapPost("/terminals/{terminalId}/revoke", async (
            string terminalId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            if (!Guid.TryParse(terminalId, out Guid parsedTerminalId))
            {
                return InvalidTerminalId();
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlTerminalActionResponse> result = await control
                .RevokeTerminalAsync(parsedTerminalId, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        group.MapPost("/terminals/{terminalId}/clear-sessions", async (
            string terminalId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LocalLanControlService control,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = LocalLanControlAuthorization.RequireLocalOperator(
                httpContext,
                sessions,
                LocalApiPermissions.StoreTerminalManage);
            if (rejected is not null)
            {
                return rejected;
            }

            if (!Guid.TryParse(terminalId, out Guid parsedTerminalId))
            {
                return InvalidTerminalId();
            }

            TerminalRegistryActorContext actor = ResolveActor(httpContext, sessions);
            Result<LocalLanControlTerminalActionResponse> result = await control
                .ClearTerminalOperatorSessionsAsync(parsedTerminalId, actor, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(result);
        });

        return app;
    }

    private static TerminalRegistryActorContext ResolveActor(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        LocalApiSession? session = LocalLanControlAuthorization.GetLocalOperatorSession(httpContext, sessions);

        if (session is null)
        {
            return new TerminalRegistryActorContext(Guid.Empty, Array.Empty<string>());
        }

        return new TerminalRegistryActorContext(session.UserId, session.Permissions.ToArray());
    }

    private static IResult InvalidRequestId()
    {
        return Results.Json(
            new
            {
                error = "A valid pending request id is required.",
                code = "RemotePosPairing.RequestIdRequired"
            },
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult InvalidTerminalId()
    {
        return Results.Json(
            new
            {
                error = "A valid terminal id is required.",
                code = "LocalLanControl.TerminalIdRequired"
            },
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult ToApiResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        return Results.Json(
            new
            {
                error = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(result.ErrorDetail.Message),
                code = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(result.ErrorDetail.Code)
            },
            statusCode: StatusCodes.Status400BadRequest);
    }
}
