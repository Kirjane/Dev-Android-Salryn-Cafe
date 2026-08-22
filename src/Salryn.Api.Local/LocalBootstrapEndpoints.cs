using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Api.Local;

internal static class LocalBootstrapEndpoints
{
    public static WebApplication MapLocalBootstrapEndpoints(
        this WebApplication app,
        SalrynLocalComposition composition,
        ILocalLicenseService? licenseService = null)
    {
        ILocalLicenseService localLicenseService = licenseService ?? new LocalLicenseService();
        app.MapGet("/api/bootstrap/status", (HttpContext httpContext) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            Result<DatabaseInitializationStatus> status = composition.DatabaseInitialization.CheckStatus();
            if (status.IsFailure)
            {
                return ToBootstrapErrorResult(status.ErrorDetail);
            }

            return Results.Ok(ToStatusResponse(status.Value));
        });

        app.MapPost("/api/bootstrap/first-admin", async (
            FirstAdminBootstrapApiRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            LocalLicenseValidationResult license = await localLicenseService
                .GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            bool validHost = license.Status == LicenseStatus.Licensed &&
                license.LicenseType == LicenseType.Host &&
                license.MachineMatched;
            bool explicitDevelopment = license.Status == LicenseStatus.Development &&
                LocalLicenseService.IsExplicitDevelopmentLicenseMode();
            if (!validHost && !explicitDevelopment)
            {
                return Results.Json(
                    new
                    {
                        error = "A valid signed Host license for this machine is required before first-run setup.",
                        code = "Bootstrap.HostLicenseRequired"
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            Result<AdminBootstrapSummary> result = composition.DatabaseInitialization.CreateFirstAdmin(
                new AdminBootstrapRequest(
                    StoreName: request.StoreName,
                    BusinessName: request.BusinessName,
                    Address: request.Address,
                    ContactNumber: request.ContactNumber,
                    Tin: request.Tin,
                    ReceiptWidthMm: request.ReceiptWidthMm,
                    AdminUsername: request.AdminUsername,
                    AdminDisplayName: request.AdminDisplayName,
                    AdminPassword: request.AdminPassword));

            if (result.IsFailure)
            {
                return ToBootstrapErrorResult(result.ErrorDetail);
            }

            Result<DatabaseInitializationStatus> status = composition.DatabaseInitialization.CheckStatus();
            if (status.IsFailure)
            {
                return ToBootstrapErrorResult(status.ErrorDetail);
            }

            return Results.Created("/api/bootstrap/first-admin", new
            {
                adminUserId = result.Value.AdminUserId,
                storeSettingsId = result.Value.StoreSettingsId,
                createdAt = result.Value.CreatedAt,
                status = ToStatusResponse(status.Value)
            });
        });

        return app;
    }

    private static object ToStatusResponse(DatabaseInitializationStatus status) => new
    {
        databaseInitialized = status.FirstRunStatus.IsDatabaseInitialized,
        databaseFileExists = status.DatabaseFileExists,
        firstRunRequired = status.FirstRunStatus.IsFirstRunRequired,
        hasUsers = status.FirstRunStatus.HasUsers,
        hasStoreSettings = status.FirstRunStatus.HasStoreSettings,
        pendingMigrationCount = status.PendingMigrationCount,
        configuredMigrationCount = status.ConfiguredMigrationCount
    };

    private static IResult ToBootstrapErrorResult(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(
            new
            {
                error = error.Message,
                code = error.Code
            },
            statusCode: statusCode);
    }
}

internal sealed record FirstAdminBootstrapApiRequest(
    string StoreName,
    string? BusinessName,
    string? Address,
    string? ContactNumber,
    string? Tin,
    int ReceiptWidthMm,
    string AdminUsername,
    string AdminDisplayName,
    string AdminPassword);
