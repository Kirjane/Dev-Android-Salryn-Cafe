using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local official setup API (RMO-BIR-M4). No LAN, no allocation, no live official posting.
/// </summary>
public static class LocalOfficialSetupEndpoints
{
    public static void MapLocalOfficialSetupEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/setup", async (
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

            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(connectionFactory);
            Result<OfficialSetupSnapshotContract> result =
                await setup.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return ToResult(result);
        });

        app.MapPut("/api/official/setup/business", async (
            OfficialSetupBusinessSaveRequest body,
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

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");

            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(connectionFactory);
            Result<OfficialSetupSnapshotContract> result =
                await setup.SaveBusinessAsync(body, userId, cancellationToken).ConfigureAwait(false);
            return ToResult(result);
        });

        app.MapPut("/api/official/setup/machine", async (
            OfficialSetupMachineSaveRequest body,
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

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");

            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(connectionFactory);
            Result<OfficialSetupSnapshotContract> result =
                await setup.SaveMachineAsync(body, userId, cancellationToken).ConfigureAwait(false);
            return ToResult(result);
        });

        app.MapPut("/api/official/setup/receipt", async (
            OfficialSetupReceiptSaveRequest body,
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

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");

            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(connectionFactory);
            Result<OfficialSetupSnapshotContract> result =
                await setup.SaveReceiptAsync(body, userId, cancellationToken).ConfigureAwait(false);
            return ToResult(result);
        });

        app.MapPut("/api/official/setup/series", async (
            OfficialSetupSeriesSaveRequest body,
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

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");

            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(connectionFactory);
            Result<OfficialSetupSnapshotContract> result =
                await setup.SaveSeriesAsync(body, userId, cancellationToken).ConfigureAwait(false);
            return ToResult(result);
        });
    }

    private static IResult ToResult(Result<OfficialSetupSnapshotContract> result)
    {
        if (result.IsFailure)
        {
            int status = result.ErrorDetail.Type == ErrorType.Validation
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;
            return Results.Json(
                new { error = result.ErrorDetail.Message, code = result.ErrorDetail.Code },
                statusCode: status);
        }

        OfficialSetupSnapshotContract snapshot = result.Value;
        return Results.Ok(new
        {
            contractVersion = snapshot.ContractVersion,
            liveOfficialPostingAllowed = false,
            summary = snapshot.Summary,
            warnings = snapshot.Warnings,
            business = snapshot.Business,
            machine = snapshot.Machine is null
                ? null
                : new
                {
                    snapshot.Machine.ProfileId,
                    snapshot.Machine.MachineSerialNumber,
                    snapshot.Machine.MachineIdentificationNumber,
                    snapshot.Machine.PosTerminalNumber,
                    snapshot.Machine.AccreditationNumber,
                    snapshot.Machine.PermitToUseNumber,
                    snapshot.Machine.SoftwareVersion,
                    snapshot.Machine.SoftwareBuild,
                    complianceMode = snapshot.Machine.ComplianceMode,
                    officialOutputEnabledInConfig = snapshot.Machine.OfficialOutputEnabledInConfig,
                    isActive = snapshot.Machine.IsActive
                },
            receipt = snapshot.Receipt,
            series = snapshot.Series is null
                ? null
                : new
                {
                    snapshot.Series.SeriesId,
                    snapshot.Series.BirMachineProfileId,
                    snapshot.Series.DocumentKind,
                    snapshot.Series.Prefix,
                    snapshot.Series.Year,
                    snapshot.Series.StartNumber,
                    snapshot.Series.CurrentNumber,
                    snapshot.Series.EndNumber,
                    snapshot.Series.PaddingDigits,
                    snapshot.Series.IsActive,
                    snapshot.Series.IsExhausted,
                    dryRunOnly = true
                },
            notes = new[]
            {
                "Official output is not implemented.",
                "Compliance is not claimed.",
                "Setup does not authorize official receipt issuance.",
                "liveOfficialPostingAllowed is false."
            }
        });
    }
}
