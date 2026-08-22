using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local RMO spine status + dry-run planner API (RMO-BIR-M7).
/// No mutation. No LAN. No official output. No live posting.
/// </summary>
public static class LocalOfficialSpineEndpoints
{
    public static void MapLocalOfficialSpineEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/spine/status", async (
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

            IHostOfficialSpineStatusService spine = new SqliteHostOfficialSpineStatusService(connectionFactory);
            Result<OfficialSpineStatusContract> result =
                await spine.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return ToSpineResult(result);
        });

        app.MapPost("/api/official/dry-run/posting-plan", async (
            OfficialDryRunPostingRequest? body,
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

            OfficialDryRunPostingRequest request = body ?? new OfficialDryRunPostingRequest(
                DocumentKind: "OFFICIAL_INVOICE",
                OperationalPosNumberHint: null,
                TaxpayerNameHint: null,
                Lines: Array.Empty<OfficialDryRunLineItem>(),
                Totals: null);

            IHostOfficialDryRunPostingPlanner planner =
                new SqliteHostOfficialDryRunPostingPlanner(connectionFactory);
            Result<OfficialDryRunPostingPlan> result =
                await planner.BuildPlanAsync(request, cancellationToken).ConfigureAwait(false);
            return ToDryRunResult(result);
        });
    }

    private static IResult ToSpineResult(Result<OfficialSpineStatusContract> result)
    {
        if (result.IsFailure)
        {
            return Results.Json(
                new { error = result.ErrorDetail.Message, code = result.ErrorDetail.Code },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        OfficialSpineStatusContract s = result.Value;
        return Results.Ok(new
        {
            contractVersion = s.ContractVersion,
            dryRunOnly = true,
            nonOfficial = true,
            liveOfficialPostingAllowed = false,
            officialCheckoutAllocationEnabled = false,
            checkoutUsesAllocator = false,
            officialOutputImplemented = false,
            complianceClaimed = false,
            components = s.Components.Select(c => new
            {
                component = c.Component.ToString(),
                level = c.Level.ToString(),
                present = c.Present,
                liveBound = false,
                summary = c.Summary
            }).ToArray(),
            blockers = s.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
            warnings = s.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
            summary = s.Summary,
            note = "Internal dry-run only. No official receipt is issued. No BIR/RMO compliance is claimed."
        });
    }

    private static IResult ToDryRunResult(Result<OfficialDryRunPostingPlan> result)
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

        OfficialDryRunPostingPlan p = result.Value;
        return Results.Ok(new
        {
            contractVersion = p.ContractVersion,
            status = p.Status.ToString(),
            saleSource = p.SaleSource.ToString(),
            dryRunOnly = true,
            nonOfficial = true,
            liveOfficialPostingAllowed = false,
            officialCheckoutAllocationEnabled = false,
            checkoutUsesAllocator = false,
            sequenceWillMutate = false,
            salesWillMutate = false,
            ejWillAppendLive = false,
            outputWillGenerate = false,
            complianceClaimed = false,
            documentKind = p.DocumentKind,
            operationalPosNumberHint = p.OperationalPosNumberHint,
            lines = p.Lines,
            totals = p.Totals,
            numbering = new
            {
                previewOnly = true,
                sequenceWillMutate = false,
                previewNextNumber = p.Numbering.PreviewNextNumber,
                formattedPreviewNumber = p.Numbering.FormattedPreviewNumber,
                prefix = p.Numbering.Prefix,
                summary = p.Numbering.Summary
            },
            journal = new
            {
                liveBound = false,
                ejWillAppendLive = false,
                skeletonPresent = p.Journal.SkeletonPresent,
                summary = p.Journal.Summary
            },
            voidRefund = new
            {
                liveIntegrated = false,
                skeletonPresent = p.VoidRefund.SkeletonPresent,
                summary = p.VoidRefund.Summary
            },
            reportPackage = new
            {
                reportDataContractPresent = true,
                reportEngineSelected = false,
                crystalSelected = false,
                outputWillGenerate = false,
                summary = p.ReportPackage.Summary
            },
            blockers = p.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
            warnings = p.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
            summary = p.Summary,
            note = "Internal dry-run only. No official receipt is issued. Checkout is not connected to official numbering. No BIR/RMO compliance is claimed."
        });
    }
}
