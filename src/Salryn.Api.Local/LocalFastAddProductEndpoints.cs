using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class LocalFastAddProductEndpoints
{
    public static WebApplication MapLocalFastAddProductEndpoints(this WebApplication app)
    {
        app.MapPost("/api/products/fast-add", async (
            FastAddProductRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            FastAddProductApiService service,
            CancellationToken cancellationToken) =>
        {
            if (RequireFastAddPermissions(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            LocalApiSession? session = sessions.GetRequiredSession(sessionId);
            if (session is null)
            {
                return Results.Json(
                    new { code = "Auth.AuthenticationRequired", error = "Authentication is required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            Result<FastAddProductResponse> result = await service
                .FastAddAsync(request, session, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToFastAddErrorResult(result.ErrorDetail);
        });

        return app;
    }

    private static IResult? RequireFastAddPermissions(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        return LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.SalesCreate) ??
               LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.ProductsView) ??
               LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.InventoryView);
    }

    private static IResult ToFastAddErrorResult(Error error)
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
