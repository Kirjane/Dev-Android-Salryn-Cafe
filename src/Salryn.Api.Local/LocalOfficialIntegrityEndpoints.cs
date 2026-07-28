using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local official integrity status API (RMO-BIR-M6).
/// Read-only. No LAN. No checkout mutation. No live official posting.
/// </summary>
public static class LocalOfficialIntegrityEndpoints
{
    public static void MapLocalOfficialIntegrityEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/integrity/status", async (
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
                    SalrynPermissions.OfficialReadingView) is IResult unauthorized)
            {
                return unauthorized;
            }

            IHostOfficialIntegrityService integrity = new SqliteHostOfficialIntegrityService(connectionFactory);
            Result<OfficialIntegrityStatusContract> result =
                await integrity.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return ToResult(result);
        });
    }

    private static IResult ToResult(Result<OfficialIntegrityStatusContract> result)
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

        OfficialIntegrityStatusContract s = result.Value;
        return Results.Ok(new
        {
            contractVersion = s.ContractVersion,
            liveOfficialPostingAllowed = false,
            checkoutIntegrated = false,
            remotePosOfficialAllowed = false,
            officialOutputImplemented = false,
            complianceClaimed = false,
            officialCheckoutAllocationEnabled = false,
            checkoutUsesAllocator = false,
            journal = new
            {
                contractVersion = s.Journal.ContractVersion,
                schemaPresent = s.Journal.SchemaPresent,
                skeletonPresent = s.Journal.SkeletonPresent,
                liveBound = false,
                liveAppendEnabled = false,
                checkoutIntegrated = false,
                hashChainPresent = false,
                exportReady = false,
                liveOfficialPostingAllowed = false,
                remotePosAllowed = false,
                complianceClaim = false,
                entryRowCount = s.Journal.EntryRowCount,
                eventRowCount = s.Journal.EventRowCount,
                appendPlan = new
                {
                    s.Journal.AppendPlan.ContractVersion,
                    liveAppendEnabled = false,
                    checkoutBound = false,
                    nonOfficial = true,
                    internalDryRun = true,
                    liveBound = false,
                    hashChainPresent = false,
                    exportReady = false,
                    plannedEventTypes = s.Journal.AppendPlan.PlannedEventTypes.Select(e => new
                    {
                        eventType = e.EventType.ToString(),
                        eventTypeCode = e.EventTypeCode,
                        plannedForOfficialSpine = e.PlannedForOfficialSpine,
                        liveAppendSupported = false
                    }).ToArray(),
                    summary = s.Journal.AppendPlan.Summary
                },
                blockers = s.Journal.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
                warnings = s.Journal.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
                summary = s.Journal.Summary
            },
            voidRefund = new
            {
                contractVersion = s.VoidRefund.ContractVersion,
                skeletonPresent = s.VoidRefund.SkeletonPresent,
                liveIntegrated = false,
                checkoutIntegrated = false,
                operationalVoidExistsSeparately = s.VoidRefund.OperationalVoidExistsSeparately,
                reasonCatalogLiveEnforced = false,
                liveOfficialPostingAllowed = false,
                remotePosAllowed = false,
                complianceClaim = false,
                linkStatus = new
                {
                    s.VoidRefund.LinkStatus.ContractVersion,
                    s.VoidRefund.LinkStatus.OriginalLinkRequired,
                    s.VoidRefund.LinkStatus.CorrectionDocumentRequired,
                    liveLinkingImplemented = false,
                    summary = s.VoidRefund.LinkStatus.Summary
                },
                placeholderReasons = s.VoidRefund.PlaceholderReasons.Select(r => new
                {
                    reasonCode = r.ReasonCode,
                    displayLabel = r.DisplayLabel,
                    isPlaceholder = true,
                    liveEnforced = false
                }).ToArray(),
                validation = new
                {
                    s.VoidRefund.Validation.ContractVersion,
                    skeletonValid = s.VoidRefund.Validation.SkeletonValid,
                    liveIntegrationAllowed = false,
                    blockers = s.VoidRefund.Validation.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
                    warnings = s.VoidRefund.Validation.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
                    summary = s.VoidRefund.Validation.Summary
                },
                blockers = s.VoidRefund.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
                warnings = s.VoidRefund.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
                summary = s.VoidRefund.Summary
            },
            warnings = s.Warnings,
            summary = s.Summary,
            note = "Official output is not implemented. Compliance is not claimed."
        });
    }
}
