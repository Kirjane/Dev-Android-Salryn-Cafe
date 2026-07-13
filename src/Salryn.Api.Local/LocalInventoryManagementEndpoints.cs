using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class LocalInventoryManagementEndpoints
{
    public static WebApplication MapLocalInventoryManagementEndpoints(this WebApplication app)
    {
        app.MapPost("/api/inventory/stock-in", async (
            StockInRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (RequireInventoryWritePermissions(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalApiSession? session = GetSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            InventoryManagementApiService service = new(composition);
            Result<StockWriteResponse> result = await service
                .StockInAsync(request, session, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToInventoryWriteErrorResult(result.ErrorDetail);
        });

        app.MapPost("/api/inventory/adjustment", async (
            StockAdjustmentRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (RequireInventoryWritePermissions(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalApiSession? session = GetSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            InventoryManagementApiService service = new(composition);
            Result<StockWriteResponse> result = await service
                .AdjustAsync(request, session, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToInventoryWriteErrorResult(result.ErrorDetail);
        });

        return app;
    }

    private static IResult? RequireInventoryWritePermissions(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        return LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.InventoryAdjust) ??
               LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.InventoryView) ??
               LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.ProductsView);
    }

    private static LocalApiSession? GetSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
        return sessions.GetRequiredSession(sessionId);
    }

    private static IResult AuthenticationRequired()
    {
        return Results.Json(
            new { code = "Auth.AuthenticationRequired", error = "Authentication is required." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static IResult ToInventoryWriteErrorResult(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(
            new { code = error.Code, error = error.Message },
            statusCode: statusCode);
    }
}
