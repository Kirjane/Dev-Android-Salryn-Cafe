using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local read-only official readiness (RMO-BIR-M3).
/// No mutation, no number allocation, no LAN client access.
/// </summary>
public static class LocalOfficialReadinessEndpoints
{
    public static void MapLocalOfficialReadinessEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/readiness", async (
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

            IOfficialCheckoutReadinessService configReadiness =
                new SqliteOfficialCheckoutReadinessService(connectionFactory);
            IHostOfficialSetupService setupService =
                new SqliteHostOfficialSetupService(connectionFactory);
            IHostOfficialReadinessService readinessService =
                new HostOfficialReadinessService(configReadiness, setupService);

            Result<HostOfficialReadinessSnapshot> result =
                await readinessService.GetReadinessAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Results.BadRequest(new
                {
                    error = result.ErrorDetail.Message,
                    code = result.ErrorDetail.Code
                });
            }

            HostOfficialReadinessSnapshot snapshot = result.Value;

            return Results.Ok(new
            {
                contractVersion = snapshot.ContractVersion,
                status = snapshot.Status.ToString(),
                officialOutputEnabledInConfig = snapshot.OfficialOutputEnabledInConfig,
                liveOfficialPostingAllowed = snapshot.LiveOfficialPostingAllowed,
                complianceMode = snapshot.ComplianceMode,
                summary = snapshot.Summary,
                allowedActions = snapshot.AllowedActions,
                blockers = snapshot.Blockers.Select(b => new { code = b.Code, message = b.Message }),
                warnings = snapshot.Warnings.Select(w => new { code = w.Code, message = w.Message }),
                businessProfile = snapshot.BusinessProfile,
                machineProfile = snapshot.MachineProfile is null
                    ? null
                    : new
                    {
                        snapshot.MachineProfile.BirMachineProfileId,
                        snapshot.MachineProfile.MachineIdentificationNumber,
                        snapshot.MachineProfile.MachineSerialNumber,
                        snapshot.MachineProfile.AccreditationNumber,
                        snapshot.MachineProfile.PermitToUseNumber,
                        snapshot.MachineProfile.ComplianceMode,
                        snapshot.MachineProfile.OfficialOutputEnabledInConfig,
                        snapshot.MachineProfile.IsActive
                    },
                notes = new[]
                {
                    "Official output is not implemented.",
                    "This endpoint is host-local only.",
                    "No BIR/RMO compliance claim."
                }
            });
        });
    }
}
