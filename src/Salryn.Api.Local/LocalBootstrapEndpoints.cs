using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Initialization;

namespace Salryn.Api.Local;

internal static class LocalBootstrapEndpoints
{
    public static WebApplication MapLocalBootstrapEndpoints(
        this WebApplication app,
        SalrynLocalComposition composition)
    {
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

        app.MapPost("/api/bootstrap/first-admin", (
            FirstAdminBootstrapApiRequest request,
            HttpContext httpContext) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
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
