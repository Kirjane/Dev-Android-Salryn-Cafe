using Salryn.Api.Local.Auth;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class ApiAuthExtensions
{
    public static IResult? RequireAuthenticatedSession(
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

    public static IResult ToErrorResult<T>(Result<T> result)
    {
        return Results.BadRequest(new { error = result.ErrorDetail.Message });
    }
}
