using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;

namespace Salryn.Api.Local;

internal static class LocalHostShiftEndpoints
{
    public static WebApplication MapLocalHostShiftEndpoints(this WebApplication app)
    {
        app.MapGet("/api/shift", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ShiftOpenClose) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalApiSession? session = GetRequiredSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            HostOperatorShiftOperationResult result = await hostOperator.ShiftService
                .ReadShiftAsync(session.UserId, cancellationToken)
                .ConfigureAwait(false);

            return ToShiftResult(result);
        });

        app.MapPost("/api/shift/open", async (
            HostOperatorShiftOpenRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ShiftOpenClose) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalApiSession? session = GetRequiredSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            HostOperatorShiftOperationResult result = await hostOperator.ShiftService
                .OpenShiftAsync(session.UserId, request.OpeningCashCents, cancellationToken)
                .ConfigureAwait(false);

            return ToShiftResult(result);
        });

        app.MapPost("/api/shift/close", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ShiftOpenClose) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalApiSession? session = GetRequiredSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            HostOperatorShiftOperationResult result = await hostOperator.ShiftService
                .CloseShiftAsync(session.UserId, cancellationToken)
                .ConfigureAwait(false);

            return ToShiftResult(result);
        });

        return app;
    }

    private static LocalApiSession? GetRequiredSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
        return sessions.GetRequiredSession(sessionId);
    }

    private static IResult AuthenticationRequired()
    {
        return Results.Json(
            new
            {
                error = "Authentication is required.",
                code = "Auth.AuthenticationRequired"
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static IResult ToShiftResult(HostOperatorShiftOperationResult result)
    {
        if (!result.Ok)
        {
            int statusCode = result.Code switch
            {
                "HostOperator.Shift.NoneToClose" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };

            return Results.Json(
                new
                {
                    error = result.Message,
                    code = result.Code
                },
                statusCode: statusCode);
        }

        return Results.Ok(new
        {
            code = result.Code,
            message = result.Message,
            shift = result.Shift
        });
    }
}