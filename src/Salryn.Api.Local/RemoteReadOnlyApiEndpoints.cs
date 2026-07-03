using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.RemoteClients;

namespace Salryn.Api.Local;

public static class RemoteReadOnlyApiEndpoints
{
    public static WebApplication MapRemoteReadOnlyEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/remote-readonly");

        group.MapGet("/status", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions),
                requestedOperation: "status",
                requestedCategory: "status",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            RemoteReadOnlyStatusResponse status = await remoteReadOnly
                .GetStatusAsync(bridgeContext, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(status);
        });

        group.MapGet("/capabilities", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions),
                requestedOperation: "capabilities",
                requestedCategory: "status",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            _ = bridgeContext;
            return Results.Ok(remoteReadOnly.GetCapabilities());
        });

        group.MapGet("/dashboard-summary", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions),
                requestedOperation: "dashboard-summary",
                requestedCategory: "dashboard",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            DashboardSummaryResponse summary = await remoteReadOnly
                .GetDashboardSummaryAsync(bridgeContext, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(summary);
        });

        group.MapGet("/products", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            string? searchText,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ProductsView),
                requestedOperation: "products",
                requestedCategory: "product",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            Result<PagedApiResponse<ProductListItemResponse>> products = await remoteReadOnly
                .SearchProductsAsync(searchText, page, pageSize, bridgeContext, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(products);
        });

        group.MapGet("/terminal-status", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.StoreTerminalView),
                requestedOperation: "terminal-status",
                requestedCategory: "terminal-status",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            RemoteReadOnlyTerminalStatusResponse terminalStatus = await remoteReadOnly
                .GetTerminalStatusAsync(bridgeContext, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(terminalStatus);
        });

        group.MapGet("/bind-policy", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions),
                requestedOperation: "bind-policy",
                requestedCategory: "status",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            _ = bridgeContext;
            RemoteReadOnlyBindPolicyResponse bindPolicy = await remoteReadOnly
                .GetBindPolicyAsync(cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(bindPolicy);
        });

        group.MapGet("/diagnostics", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            RemoteReadOnlyBridgeRequestContext bridgeRequest =
                RemoteReadOnlyApiAuthorization.TryGetRemoteBridgeContext(httpContext);

            if (bridgeRequest.IsRemoteBridgeRequest)
            {
                (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await RemoteReadOnlyApiAuthorization.AuthorizeBridgeAsync(
                    bridgeRequest,
                    lanServices,
                    requestedOperation: "diagnostics",
                    requestedCategory: "status",
                    cancellationToken).ConfigureAwait(false);

                if (rejected is not null)
                {
                    return rejected;
                }

                _ = bridgeContext;
            }
            else
            {
                IResult? localhostRejected = LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions);
                if (localhostRejected is not null)
                {
                    return localhostRejected;
                }
            }

            RemoteReadOnlyDiagnosticsResponse diagnostics = await remoteReadOnly
                .GetDiagnosticsAsync(bridgeRequest, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(diagnostics);
        });

        group.MapGet("/audit", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            LanRemoteTransportApiServices lanServices,
            RemoteReadOnlyApiService remoteReadOnly,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemoteReadOnlyClientContext? bridgeContext) = await AuthorizeBridgeOrLocalhostAsync(
                httpContext,
                sessions,
                lanServices,
                () => LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.AuditView),
                requestedOperation: "audit",
                requestedCategory: "audit",
                cancellationToken).ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            _ = bridgeContext;
            RemoteReadOnlyAuditVisibilityResponse audit = await remoteReadOnly
                .GetAuditVisibilityAsync(page, pageSize, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(audit);
        });

        return app;
    }

    private static async Task<(IResult? Rejected, RemoteReadOnlyClientContext? BridgeContext)> AuthorizeBridgeOrLocalhostAsync(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        LanRemoteTransportApiServices lanServices,
        Func<IResult?> localhostGuard,
        string requestedOperation,
        string requestedCategory,
        CancellationToken cancellationToken)
    {
        _ = sessions;
        RemoteReadOnlyBridgeRequestContext bridgeRequest =
            RemoteReadOnlyApiAuthorization.TryGetRemoteBridgeContext(httpContext);

        if (bridgeRequest.IsRemoteBridgeRequest)
        {
            return await RemoteReadOnlyApiAuthorization.AuthorizeBridgeAsync(
                bridgeRequest,
                lanServices,
                requestedOperation,
                requestedCategory,
                cancellationToken).ConfigureAwait(false);
        }

        return (localhostGuard(), null);
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
