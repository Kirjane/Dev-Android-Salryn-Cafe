using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.Services.Licensing;

namespace Salryn.Api.Local;

internal static class LocalUserAdminEndpoints
{
    public static WebApplication MapLocalUserAdminEndpoints(this WebApplication app)
    {
        app.MapPost("/api/users", async (
            CreateUserApiRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            UserAdminApiService users,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.UsersManage) is IResult unauthorized)
            {
                return unauthorized;
            }

            if (await ApiLicenseGuard.RequireLicensedFeatureAsync(LicenseFeatures.Users, cancellationToken)
                    .ConfigureAwait(false) is IResult licenseDenied)
            {
                return licenseDenied;
            }

            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            LocalApiSession? session = sessions.GetRequiredSession(sessionId);
            if (session is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Authentication is required.",
                        code = "Auth.AuthenticationRequired"
                    },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            Result<UserListItemResponse> result = await users
                .CreateUserAsync(request, session.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                return ToUserAdminErrorResult(result.ErrorDetail);
            }

            return Results.Created($"/api/users/{result.Value.Id}", result.Value);
        });

        return app;
    }

    private static IResult ToUserAdminErrorResult(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(
            new
            {
                error = error.Message,
                code = error.Code
            },
            statusCode: statusCode);
    }
}
