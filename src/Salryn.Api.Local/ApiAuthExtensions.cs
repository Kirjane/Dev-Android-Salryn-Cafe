using Salryn.Api.Local.Auth;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class ApiAuthExtensions
{
    public static IResult? RequireAuthenticatedSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        return LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions);
    }

    public static IResult ToErrorResult<T>(Result<T> result)
    {
        return Results.BadRequest(new { error = result.ErrorDetail.Message });
    }
}
