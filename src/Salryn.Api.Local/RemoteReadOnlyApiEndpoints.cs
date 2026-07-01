using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

public static class RemoteReadOnlyApiEndpoints
{
    public static WebApplication MapRemoteReadOnlyEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/remote-readonly");

        group.MapGet("/status", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            return rejected ?? Results.Ok(remoteReadOnly.GetStatus());
        });

        group.MapGet("/capabilities", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            return rejected ?? Results.Ok(remoteReadOnly.GetCapabilities());
        });

        group.MapGet("/dashboard-summary", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            if (rejected is not null)
            {
                return rejected;
            }

            DashboardSummaryResponse summary = await remoteReadOnly
                .GetDashboardSummaryAsync(cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(summary);
        });

        group.MapGet("/products", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly,
            string? searchText,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            if (rejected is not null)
            {
                return rejected;
            }

            Result<PagedApiResponse<ProductListItemResponse>> products = await remoteReadOnly
                .SearchProductsAsync(searchText, page, pageSize, cancellationToken)
                .ConfigureAwait(false);

            return ToApiResult(products);
        });

        group.MapGet("/terminal-status", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            return rejected ?? Results.Ok(remoteReadOnly.GetTerminalStatus());
        });

        group.MapGet("/bind-policy", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            return rejected ?? Results.Ok(remoteReadOnly.GetBindPolicy());
        });

        group.MapGet("/audit", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            RemoteReadOnlyApiService remoteReadOnly,
            int page,
            int pageSize) =>
        {
            IResult? rejected = RequireSession(httpContext, sessions);
            return rejected ?? Results.Ok(remoteReadOnly.GetAuditVisibility(page, pageSize));
        });

        return app;
    }

    private static IResult? RequireSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
        if (sessions.GetRequiredSession(sessionId) is null)
        {
            return Results.Json(
                new { error = "Authentication is required." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return null;
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
                error = result.ErrorDetail.Message,
                code = result.ErrorDetail.Code
            },
            statusCode: StatusCodes.Status400BadRequest);
    }
}
