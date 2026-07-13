using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class LocalProductManagementEndpoints
{
    public static WebApplication MapLocalProductManagementEndpoints(this WebApplication app)
    {
        app.MapPost("/api/products/manage", async (
            ProductManagementWriteRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            ProductManagementApiService service,
            CancellationToken cancellationToken) =>
        {
            if (RequireProductManagementPermissions(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<ProductManagementWriteResponse> result = await service
                .CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToProductManagementErrorResult(result.ErrorDetail);
        });

        app.MapPut("/api/products/{id:guid}/manage", async (
            Guid id,
            ProductManagementWriteRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            ProductManagementApiService service,
            CancellationToken cancellationToken) =>
        {
            if (RequireProductManagementPermissions(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<ProductManagementWriteResponse> result = await service
                .UpdateAsync(id, request, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToProductManagementErrorResult(result.ErrorDetail);
        });

        app.MapPost("/api/products/{id:guid}/deactivate", async (
            Guid id,
            ProductDeactivateRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            ProductManagementApiService service,
            CancellationToken cancellationToken) =>
        {
            if (RequireProductManagementPermissions(httpContext, sessions) is IResult unauthorized)
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

            Result<ProductDeactivateResponse> result = await service
                .DeactivateAsync(id, request, session, cancellationToken)
                .ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : ToProductManagementErrorResult(result.ErrorDetail);
        });

        return app;
    }

    private static IResult? RequireProductManagementPermissions(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        return LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.ProductsManage) ??
               LocalApiPermissionGuards.RequirePermission(
                   httpContext,
                   sessions,
                   LocalApiPermissions.ProductsView);
    }

    private static IResult ToProductManagementErrorResult(Error error)
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
