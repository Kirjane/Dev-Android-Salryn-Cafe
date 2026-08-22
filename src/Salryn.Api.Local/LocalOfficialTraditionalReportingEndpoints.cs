using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services.Reporting;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-only traditional reporting (X/Z/EJ/eSales draft). Lifecycle-aware. No BIR transmission.
/// </summary>
public static class LocalOfficialTraditionalReportingEndpoints
{
    public static void MapLocalOfficialTraditionalReportingEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/traditional-reporting/status", (
            HttpContext httpContext,
            LocalApiSessionService sessions) =>
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

            return Results.Json(Compose(connectionFactory).GetStatus().Value);
        });

        app.MapPost("/api/official/traditional-reporting/x-reading", async (
            ReportingRangeApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (Guard(httpContext, sessions, SalrynPermissions.OfficialReadingView) is IResult denied)
            {
                return denied;
            }

            if (!TryParseRequest(body, out OfficialReportingProjectionRequest request, out IResult? bad))
            {
                return bad!;
            }

            Result<TraditionalXReadingView> result =
                await Compose(connectionFactory).PreviewXReadingAsync(request, cancellationToken).ConfigureAwait(false);
            return ToJson(result);
        });

        app.MapPost("/api/official/traditional-reporting/z-reading/finalize", async (
            ReportingRangeApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (Guard(httpContext, sessions, SalrynPermissions.OfficialReadingFinalize) is IResult denied)
            {
                return denied;
            }

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");
            if (!Guid.TryParse(userId, out Guid actorId))
            {
                return Results.Json(
                    new { code = "OfficialReporting.ActorRequired", message = "Authenticated actor required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!TryParseRequest(body, out OfficialReportingProjectionRequest request, out IResult? bad))
            {
                return bad!;
            }

            Result<TraditionalZReadingView> result = await Compose(connectionFactory)
                .FinalizeZReadingAsync(request, actorId, body.TerminalId, body.FailureInjectionStage, cancellationToken)
                .ConfigureAwait(false);
            return ToJson(result);
        });

        app.MapPost("/api/official/traditional-reporting/ej-export", async (
            ReportingRangeApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (Guard(httpContext, sessions, SalrynPermissions.OfficialEvidenceExport) is IResult denied)
            {
                return denied;
            }

            if (!TryParseRequest(body, out OfficialReportingProjectionRequest request, out IResult? bad))
            {
                return bad!;
            }

            Result<TraditionalReportingExportPackage> result =
                await Compose(connectionFactory).ExportEjAsync(request, cancellationToken).ConfigureAwait(false);
            return ToExportJson(result);
        });

        app.MapPost("/api/official/traditional-reporting/esales-draft", async (
            ReportingRangeApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (Guard(httpContext, sessions, SalrynPermissions.OfficialEvidenceExport) is IResult denied)
            {
                return denied;
            }

            if (!TryParseRequest(body, out OfficialReportingProjectionRequest request, out IResult? bad))
            {
                return bad!;
            }

            Result<TraditionalReportingExportPackage> result =
                await Compose(connectionFactory).ExportESalesDraftAsync(request, cancellationToken).ConfigureAwait(false);
            return ToExportJson(result);
        });

        app.MapGet("/api/official/traditional-reporting/grand-total", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (Guard(httpContext, sessions, SalrynPermissions.OfficialReadingView) is IResult denied)
            {
                return denied;
            }

            Result<TraditionalGrandTotalSnapshot> result =
                await Compose(connectionFactory).GetGrandTotalSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return ToJson(result);
        });
    }

    private static IResult? Guard(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        return LocalApiPermissionGuards.RequirePermission(httpContext, sessions, permission);
    }

    private static bool TryParseRequest(
        ReportingRangeApiRequest body,
        out OfficialReportingProjectionRequest request,
        out IResult? error)
    {
        request = default!;
        error = null;
        if (!DateTimeOffset.TryParse(body.RangeStart, out DateTimeOffset start) ||
            !DateTimeOffset.TryParse(body.RangeEnd, out DateTimeOffset end))
        {
            error = Results.Json(
                new { code = "OfficialReporting.InvalidRange", message = "RangeStart/RangeEnd required (ISO-8601)." },
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        Guid? machineId = Guid.TryParse(body.BirMachineProfileId, out Guid m) ? m : null;
        ElectronicJournalDocumentKind kind =
            string.Equals(body.DocumentKind, "OFFICIAL_RECEIPT", StringComparison.OrdinalIgnoreCase)
                ? ElectronicJournalDocumentKind.OfficialReceipt
                : ElectronicJournalDocumentKind.OfficialInvoice;

        request = new OfficialReportingProjectionRequest(
            start,
            end,
            machineId,
            string.IsNullOrWhiteSpace(body.RegistrationScopeCode) ? "LOCAL_SINGLE_STORE" : body.RegistrationScopeCode.Trim(),
            kind);
        return true;
    }

    private static IResult ToJson<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            int code = result.ErrorDetail.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };
            return Results.Json(
                new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                statusCode: code);
        }

        return Results.Json(result.Value);
    }

    private static IResult ToExportJson(Result<TraditionalReportingExportPackage> result)
    {
        if (result.IsFailure)
        {
            return ToJson(result);
        }

        TraditionalReportingExportPackage p = result.Value;
        return Results.Json(new
        {
            kind = p.Kind,
            schemaCode = p.SchemaCode,
            rangeStart = p.RangeStart,
            rangeEnd = p.RangeEnd,
            contentSha256 = p.ContentSha256,
            rowOrEventCount = p.RowOrEventCount,
            controlTotalCents = p.ControlTotalCents,
            birAuthoritativeSchemaConfirmed = p.BirAuthoritativeSchemaConfirmed,
            transmissionEnabled = p.TransmissionEnabled,
            summary = p.Summary,
            utf8Content = p.Utf8Content,
            projection = new
            {
                p.Projection.GrossPostedSalesCents,
                p.Projection.VoidedSalesCents,
                p.Projection.RefundedValueCents,
                p.Projection.NetSalesCents,
                p.Projection.VatableSalesCents,
                p.Projection.VatAmountCents,
                p.Projection.ProjectionContentSha256
            }
        });
    }

    private static IHostTraditionalReportingService Compose(SqliteConnectionFactory factory) =>
        new HostTraditionalReportingService(
            new SqliteOfficialReportingProjectionService(factory),
            factory);

    public sealed record ReportingRangeApiRequest(
        string? RangeStart,
        string? RangeEnd,
        string? BirMachineProfileId,
        string? RegistrationScopeCode,
        string? DocumentKind,
        string? TerminalId,
        string? FailureInjectionStage);
}
