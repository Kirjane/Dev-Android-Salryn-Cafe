using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Api.Local.Auth;

internal static class ApiLicenseGuard
{
    public static ILocalLicenseEnforcementService CreateDefaultEnforcement() =>
        new LocalLicenseEnforcementService(new LocalLicenseService());

    public static async Task<IResult?> RequireLicensedFeatureAsync(
        string feature,
        CancellationToken cancellationToken = default) =>
        await RequireLicensedFeatureAsync(CreateDefaultEnforcement(), feature, cancellationToken)
            .ConfigureAwait(false);

    public static async Task<IResult?> RequireLicensedFeatureAsync(
        ILocalLicenseEnforcementService licensing,
        string feature,
        CancellationToken cancellationToken = default)
    {
        LicenseEnforcementDecision decision = await licensing
            .RequireFeatureAsync(feature, cancellationToken)
            .ConfigureAwait(false);

        if (decision.Allowed)
        {
            return null;
        }

        return Results.Json(
            new LicenseDeniedApiResponse(
                "License.FeatureDenied",
                decision.Message,
                decision.RequiredFeature,
                decision.LicenseStatus,
                decision.ReasonCode,
                decision.EnabledFeatures),
            statusCode: StatusCodes.Status403Forbidden);
    }

    private sealed record LicenseDeniedApiResponse(
        string Code,
        string Error,
        string RequiredFeature,
        LicenseStatus LicenseStatus,
        LicenseEnforcementReason ReasonCode,
        IReadOnlyList<string> EnabledFeatures);
}
