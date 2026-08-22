using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.Services.Licensing;

namespace Salryn.Api.Local;

internal static class LocalOwnerReportsEndpoints
{
    public static WebApplication MapLocalOwnerReportsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/owner-reports/summary", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OwnerReportsApiService service,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(httpContext, sessions, LocalApiPermissions.ReportsView) is IResult denied)
            {
                return denied;
            }

            if (await ApiLicenseGuard.RequireLicensedFeatureAsync(LicenseFeatures.Reports, cancellationToken)
                    .ConfigureAwait(false) is IResult licenseDenied)
            {
                return licenseDenied;
            }

            Result<OwnerReportsResponse> result = await service.GetSummaryAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return Results.Ok(result.Value);
            }

            return Results.Json(
                new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message },
                statusCode: StatusCodes.Status400BadRequest);
        });

        return app;
    }
}
