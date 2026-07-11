using System.Text.Json;
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

        app.MapPost("/api/license/install", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SettingsManage) is IResult unauthorized)
            {
                return unauthorized;
            }

            LocalLicenseInstallEnvelopeReadResult read = await ReadInstallRequestAsync(
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false);

            if (read.Error is not null)
            {
                return Results.Json(read.Error, statusCode: StatusCodes.Status400BadRequest);
            }

            LocalLicenseInstallResponse result = await localLicenseService
                .InstallAsync(read.Request!, cancellationToken)
                .ConfigureAwait(false);

            int statusCode = string.Equals(result.InstallStatus, "Installed", StringComparison.Ordinal)
                ? StatusCodes.Status200OK
                : StatusCodes.Status400BadRequest;

            return Results.Json(result, statusCode: statusCode);
        });
    }

    private static async Task<LocalLicenseInstallEnvelopeReadResult> ReadInstallRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new(httpContext.Request.Body);
        string body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return LocalLicenseInstallEnvelopeReadResult.WithRequest(new LocalLicenseInstallRequest(null));
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetLicenseJson(parsed.RootElement, out JsonElement licenseJson))
            {
                foreach (JsonProperty property in parsed.RootElement.EnumerateObject())
                {
                    if (!string.Equals(
                            property.Name,
                            nameof(LocalLicenseInstallRequest.LicenseJson),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return LocalLicenseInstallEnvelopeReadResult.WithError(RequestRejected(
                            "License.Install.UnsupportedRequestField",
                            "License install request contains an unsupported field: " + property.Name));
                    }
                }

                if (licenseJson.ValueKind != JsonValueKind.String)
                {
                    return LocalLicenseInstallEnvelopeReadResult.WithError(RequestRejected(
                        "License.Install.InvalidRequestDocument",
                        "LicenseJson must contain the license document JSON as a string."));
                }

                return LocalLicenseInstallEnvelopeReadResult.WithRequest(
                    new LocalLicenseInstallRequest(licenseJson.GetString()));
            }
        }
        catch (JsonException)
        {
            return LocalLicenseInstallEnvelopeReadResult.WithRequest(new LocalLicenseInstallRequest(body));
        }

        return LocalLicenseInstallEnvelopeReadResult.WithRequest(new LocalLicenseInstallRequest(body));
    }

    private static bool TryGetLicenseJson(
        JsonElement root,
        out JsonElement licenseJson)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    nameof(LocalLicenseInstallRequest.LicenseJson),
                    StringComparison.OrdinalIgnoreCase))
            {
                licenseJson = property.Value;
                return true;
            }
        }

        licenseJson = default;
        return false;
    }

    private static LocalLicenseInstallResponse RequestRejected(
        string errorCode,
        string errorMessage) => new(
            "Rejected",
            LicenseStatus.Invalid,
            null,
            null,
            errorCode,
            errorMessage,
            DateTimeOffset.UtcNow);

    private sealed record LocalLicenseInstallEnvelopeReadResult(
        LocalLicenseInstallRequest? Request,
        LocalLicenseInstallResponse? Error)
    {
        public static LocalLicenseInstallEnvelopeReadResult WithRequest(
            LocalLicenseInstallRequest request) => new(request, Error: null);

        public static LocalLicenseInstallEnvelopeReadResult WithError(
            LocalLicenseInstallResponse error) => new(null, error);
    }
}
