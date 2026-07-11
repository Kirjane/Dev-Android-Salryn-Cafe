using Salryn.Api.Local.Auth;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Api.Local;

public static class LocalLicenseEndpoints
{
    public static void MapLocalLicenseEndpoints(
        this WebApplication app,
        ILocalLicenseService? licenseService = null)
    {
        ILocalLicenseService localLicenseService = licenseService ?? new LocalLicenseService();

        app.MapGet("/api/license/status", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            if (ApiAuthExtensions.RequireAuthenticatedSession(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalLicenseValidationResult result = await localLicenseService
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            return Results.Json(result);
        });
    }
}
