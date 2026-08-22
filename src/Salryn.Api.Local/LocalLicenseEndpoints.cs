using System.Text.Json;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Api.Local;

public static class LocalLicenseEndpoints
{
    public static void MapLocalLicenseEndpoints(
        this WebApplication app,
        ILocalLicenseService? licenseService = null,
        ILocalLicenseEnforcementService? enforcementService = null,
        SalrynLocalComposition? composition = null)
    {
        ILocalLicenseService localLicenseService = licenseService ?? new LocalLicenseService();
        ILocalLicenseEnforcementService localLicenseEnforcementService = enforcementService ?? new LocalLicenseEnforcementService(localLicenseService);

        app.MapGet("/api/license/status", async (
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            LocalLicenseValidationResult result = await localLicenseService
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            return Results.Json(result);
        });

        app.MapGet("/api/license/entitlements", async (
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

            LocalLicenseValidationResult status = await localLicenseService
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            List<LicenseEnforcementDecision> decisions = new();
            foreach (string feature in LicenseFeatures.AllList)
            {
                decisions.Add(await localLicenseEnforcementService
                    .RequireFeatureAsync(feature, cancellationToken)
                    .ConfigureAwait(false));
            }

            return Results.Json(new LocalLicenseEntitlementsResponse(
                status.Status,
                status.EnabledFeatures,
                LicenseFeatures.AllList,
                decisions,
                status.MachineMatched,
                status.Message,
                status.ValidationTimestamp,
                status.LicenseType,
                status.HostLicenseId,
                status.TerminalId,
                status.MachineCode ?? status.MachineFingerprint,
                status.AllowedLanTerminals));
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

            LocalLicenseValidationResult before = await localLicenseService
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            // First install/recovery must remain possible before an operator account exists. Once a
            // valid license state is present, installation is replacement/update and requires the
            // same Settings.Manage boundary as other host licensing administration.
            if (RequiresPrivilegedInstallAuthorization(before.Status) &&
                LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SettingsManage) is IResult denied)
            {
                return denied;
            }

            if (!httpContext.Request.HasJsonContentType())
            {
                return Results.Json(
                    RequestRejected(
                        "License.Install.JsonContentTypeRequired",
                        "License install requires an application/json request."),
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }

            LocalLicenseInstallEnvelopeReadResult read = await ReadInstallRequestAsync(
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false);

            if (read.Error is not null)
            {
                return Results.Json(read.Error, statusCode: StatusCodes.Status400BadRequest);
            }

            bool hadLicense = before.Status is LicenseStatus.Licensed or LicenseStatus.Grace or LicenseStatus.Development;

            LocalLicenseInstallResponse result = await localLicenseService
                .InstallAsync(read.Request!, cancellationToken)
                .ConfigureAwait(false);

            int statusCode = string.Equals(result.InstallStatus, "Installed", StringComparison.Ordinal)
                ? StatusCodes.Status200OK
                : StatusCodes.Status400BadRequest;

            if (statusCode == StatusCodes.Status200OK && composition is not null)
            {
                LocalLicenseValidationResult after = await localLicenseService
                    .GetStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
                string action = hadLicense || !string.IsNullOrWhiteSpace(result.BackupPath)
                    ? OperatorAuditActions.LicenseReplaced
                    : OperatorAuditActions.LicenseInstalled;

                await OperatorAuditTrail.TryWriteAsync(
                        composition.ConnectionFactory,
                        userId: null,
                        action,
                        "License",
                        after.LicenseId,
                        new
                        {
                            licenseId = after.LicenseId,
                            licenseType = after.LicenseType?.ToString(),
                            customerName = after.CustomerName,
                            storeId = after.StoreId,
                            status = after.Status.ToString()
                        },
                        reason: hadLicense ? "Signed local license replaced." : "Signed local license installed.",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            return Results.Json(result, statusCode: statusCode);
        });
    }

    public static bool RequiresPrivilegedInstallAuthorization(LicenseStatus status) =>
        status is LicenseStatus.Licensed or LicenseStatus.Grace or LicenseStatus.Development;

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
