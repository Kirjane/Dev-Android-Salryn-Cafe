using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local official numbering preview/validate API (RMO-BIR-M5).
/// No sequence mutation. No LAN. No checkout allocation. No live official posting.
/// </summary>
public static class LocalOfficialNumberingEndpoints
{
    public static void MapLocalOfficialNumberingEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/numbering/preview", async (
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

            IHostOfficialNumberingService numbering = new SqliteHostOfficialNumberingService(connectionFactory);
            Result<OfficialNumberingPreviewContract> result =
                await numbering.GetPreviewAsync(cancellationToken).ConfigureAwait(false);
            return ToPreviewResult(result);
        });

        app.MapPost("/api/official/numbering/validate", async (
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

            IHostOfficialNumberingService numbering = new SqliteHostOfficialNumberingService(connectionFactory);
            Result<OfficialNumberingValidationResult> result =
                await numbering.ValidateSeriesAsync(cancellationToken).ConfigureAwait(false);
            return ToValidateResult(result);
        });
    }

    private static IResult ToPreviewResult(Result<OfficialNumberingPreviewContract> result)
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

        OfficialNumberingPreviewContract p = result.Value;
        return Results.Ok(new
        {
            contractVersion = p.ContractVersion,
            documentType = p.DocumentType,
            prefix = p.Prefix,
            currentNumber = p.CurrentNumber,
            nextNumber = p.NextNumber,
            formattedNextNumber = p.FormattedNextNumber,
            rangeStart = p.RangeStart,
            rangeEnd = p.RangeEnd,
            sequenceScopeCode = p.SequenceScopeCode,
            birMachineProfileId = p.BirMachineProfileId,
            seriesId = p.SeriesId,
            dryRunOnly = true,
            liveOfficialPostingAllowed = false,
            officialCheckoutAllocationEnabled = false,
            checkoutUsesAllocator = false,
            previewMutatesSequence = false,
            allocationAllowed = false,
            mode = p.Mode.ToString(),
            series = p.Series,
            blockers = p.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
            warnings = p.Warnings,
            summary = p.Summary,
            blockedReason = p.BlockedReason,
            note = "Official output is not implemented. Compliance is not claimed."
        });
    }

    private static IResult ToValidateResult(Result<OfficialNumberingValidationResult> result)
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

        OfficialNumberingValidationResult v = result.Value;
        return Results.Ok(new
        {
            contractVersion = v.ContractVersion,
            isValidForPreview = v.IsValidForPreview,
            liveAllocationAllowed = false,
            officialCheckoutAllocationEnabled = false,
            checkoutUsesAllocator = false,
            liveOfficialPostingAllowed = false,
            series = v.Series,
            previewNextNumber = v.PreviewNextNumber,
            formattedNextNumber = v.FormattedNextNumber,
            blockers = v.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
            warnings = v.Warnings,
            summary = v.Summary,
            note = "Official output is not implemented. Compliance is not claimed."
        });
    }
}
