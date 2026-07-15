using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Services.Licensing;

namespace Salryn.Api.Local;

internal static class LocalHostCheckoutEndpoints
{
    public static WebApplication MapLocalHostCheckoutEndpoints(this WebApplication app)
    {
        app.MapPost("/api/checkout", async (
            HostOperatorCheckoutRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SalesCreate) is IResult unauthorized)
            {
                return unauthorized;
            }

            if (await ApiLicenseGuard.RequireLicensedFeatureAsync(LicenseFeatures.Pos, cancellationToken)
                    .ConfigureAwait(false) is IResult licenseDenied)
            {
                return licenseDenied;
            }

            LocalApiSession? session = GetRequiredSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            HostOperatorCheckoutOperationResult result = await hostOperator.CheckoutService
                .SubmitAsync(session.UserId, request, cancellationToken)
                .ConfigureAwait(false);

            return ToCheckoutResult(result);
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

    private static IResult ToCheckoutResult(HostOperatorCheckoutOperationResult result)
    {
        if (!result.Ok)
        {
            int statusCode = result.Checkout?.Status switch
            {
                HostOperatorCheckoutStatus.OpenShiftRequired => StatusCodes.Status409Conflict,
                HostOperatorCheckoutStatus.StockChanged => StatusCodes.Status409Conflict,
                HostOperatorCheckoutStatus.ProductUnavailable => StatusCodes.Status404NotFound,
                HostOperatorCheckoutStatus.PaymentInsufficient => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status400BadRequest
            };

            return Results.Json(
                new
                {
                    error = result.Message,
                    code = result.Code,
                    checkout = result.Checkout
                },
                statusCode: statusCode);
        }

        return Results.Ok(new
        {
            code = result.Code,
            message = result.Message,
            checkout = result.Checkout
        });
    }
}