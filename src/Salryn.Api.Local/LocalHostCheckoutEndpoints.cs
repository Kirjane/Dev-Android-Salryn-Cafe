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
            ILocalLicenseEnforcementService licensing,
            CancellationToken cancellationToken) =>
        {
            if (ApiAuthExtensions.RequireAuthenticatedSession(httpContext, sessions) is IResult unauthenticated)
            {
                return unauthenticated;
            }

            bool createsReceivable = request.Payments?.Any(payment =>
                string.Equals(payment.Method, "AccountsReceivable", StringComparison.OrdinalIgnoreCase)) == true;

            if (await ApiLicenseGuard.RequireLicensedFeatureAsync(licensing, LicenseFeatures.Pos, cancellationToken)
                    .ConfigureAwait(false) is IResult licenseDenied)
            {
                return licenseDenied;
            }

            if (createsReceivable &&
                await ApiLicenseGuard.RequireLicensedFeatureAsync(
                        licensing,
                        LicenseFeatures.ReceivablesUtangLedger,
                        cancellationToken)
                    .ConfigureAwait(false) is IResult receivablesLicenseDenied)
            {
                return receivablesLicenseDenied;
            }

            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SalesCreate) is IResult unauthorized)
            {
                return unauthorized;
            }

            if (createsReceivable &&
                LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ReceivablesCreateCreditSale) is IResult creditDenied)
            {
                return creditDenied;
            }

            LocalApiSession? session = GetRequiredSession(httpContext, sessions);
            if (session is null)
            {
                return AuthenticationRequired();
            }

            HostOperatorCheckoutOperationResult result = await hostOperator.CheckoutService
                .SubmitAsync(
                    session.UserId,
                    ResolveCashierDisplayName(session),
                    request,
                    cancellationToken)
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

    private static string ResolveCashierDisplayName(LocalApiSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return session.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(session.Username)
            ? "Cashier"
            : session.Username.Trim();
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
