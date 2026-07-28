using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local guarded live posting candidate API (RMO-BIR-M11).
/// Planning only. No mutation. No official output. No LAN/Remote POS. No checkout bind.
/// </summary>
public static class LocalOfficialLivePostingCandidateEndpoints
{
    public static void MapLocalOfficialLivePostingCandidateEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/live-posting-candidate/status", async (
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

            IHostOfficialLivePostingCandidateService service =
                new SqliteHostOfficialLivePostingCandidateService(connectionFactory);
            Result<OfficialLivePostingCandidateStatusContract> result =
                await service.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Fail(result.ErrorDetail);
            }

            OfficialLivePostingCandidateStatusContract s = result.Value;
            return Results.Ok(new
            {
                contractVersion = s.ContractVersion,
                candidateServicePresent = true,
                candidateOnly = true,
                disabledByDefault = true,
                dryRunOnly = true,
                nonOfficial = true,
                liveOfficialPostingAllowed = false,
                checkoutBound = false,
                remotePosAllowed = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                eisWillTransmit = false,
                officialReceiptIssued = false,
                complianceClaimed = false,
                blocked = true,
                eisJsonSchemaResolved = false,
                defaultTrack = s.DefaultTrack.ToString(),
                summary = s.Summary,
                note = "Live posting candidate: blocked. Internal candidate plan only. No official number is consumed."
            });
        });

        app.MapPost("/api/official/live-posting-candidate/plan", async (
            CandidatePlanBody? body,
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

            OfficialLivePostingCandidateTrack? preferred = null;
            if (!string.IsNullOrWhiteSpace(body?.PreferredTrack) &&
                Enum.TryParse(body.PreferredTrack, ignoreCase: true, out OfficialLivePostingCandidateTrack parsed))
            {
                preferred = parsed;
            }

            OfficialLivePostingCandidateRequest request = new(
                OperationalSaleId: body?.SaleId,
                DocumentKind: body?.DocumentKind,
                ExplicitInternalActivationRequested: body?.ExplicitInternalActivationRequested == true,
                PreferredTrack: preferred,
                PreferEisTrack: body?.PreferEisTrack == true);

            IHostOfficialLivePostingCandidateService service =
                new SqliteHostOfficialLivePostingCandidateService(connectionFactory);
            Result<OfficialLivePostingCandidateResult> result =
                await service.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Fail(result.ErrorDetail);
            }

            OfficialLivePostingCandidateResult r = result.Value;
            return Results.Ok(new
            {
                contractVersion = r.ContractVersion,
                candidateOnly = true,
                disabledByDefault = true,
                dryRunOnly = true,
                nonOfficial = true,
                liveOfficialPostingAllowed = false,
                officialCheckoutAllocationEnabled = false,
                checkoutUsesAllocator = false,
                checkoutBound = false,
                remotePosAllowed = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                eisWillTransmit = false,
                officialReceiptIssued = false,
                complianceClaimed = false,
                productionActivation = false,
                blocked = true,
                selectedTrack = r.SelectedTrack.ToString(),
                terminalStage = r.TerminalStage.ToString(),
                sourceSaleId = r.SourceSaleId,
                documentKind = r.DocumentKind,
                numberingPreviewHint = r.NumberingPreviewHint,
                atomicPlan = new
                {
                    r.AtomicPlan.SameTransactionForNumberSaleAndEj,
                    r.AtomicPlan.AllocateOnlyOnCommit,
                    r.AtomicPlan.NoDraftCartAllocation,
                    r.AtomicPlan.NoRemoteClientAllocation,
                    summary = r.AtomicPlan.Summary
                },
                failurePlan = new
                {
                    r.FailurePlan.NumberNotConsumedOnPreAllocateFailure,
                    r.FailurePlan.RetryOutputUsesSameNumber,
                    r.FailurePlan.CrashLeavesNoOrphanAllocatedNumber,
                    summary = r.FailurePlan.Summary
                },
                outputPlan = new
                {
                    outputWillGenerate = false,
                    crystalSelected = false,
                    pdfSelected = false,
                    printerSelected = false,
                    watermarkOnlyInternalPath = true,
                    summary = r.OutputPlan.Summary
                },
                transmissionPlan = new
                {
                    track = r.TransmissionPlan.Track.ToString(),
                    eisWillTransmit = false,
                    traditionalESalesWillPush = false,
                    monthlyESalesExportPlanned = r.TransmissionPlan.MonthlyESalesExportPlanned,
                    eisJsonSchemaResolved = false,
                    summary = r.TransmissionPlan.Summary
                },
                stages = r.Stages.Select(s => new
                {
                    stage = s.Stage.ToString(),
                    planned = s.Planned,
                    mutatesState = false,
                    summary = s.Summary
                }).ToArray(),
                invariants = r.Invariants.Select(i => new
                {
                    code = i.Code,
                    description = i.Description,
                    satisfiedInPlan = i.SatisfiedInPlan
                }).ToArray(),
                blockers = r.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
                warnings = r.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
                summary = r.Summary,
                note =
                    "Live posting candidate: blocked. Internal candidate plan only. " +
                    "No official number is consumed. No receipt or invoice is issued. Checkout is not connected."
            });
        });
    }

    private static IResult Fail(Error error)
    {
        int status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new { error = error.Message, code = error.Code }, statusCode: status);
    }

    private sealed record CandidatePlanBody(
        Guid? SaleId,
        string? DocumentKind,
        string? PreferredTrack,
        bool? PreferEisTrack,
        bool? ExplicitInternalActivationRequested);
}
