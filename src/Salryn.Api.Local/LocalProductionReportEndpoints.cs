using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Services.Reporting;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Reports;

namespace Salryn.Api.Local;

/// <summary>
/// PRE-CRYSTAL M0 / Crystal-M1 host-only production reporting spine.
/// Dataset generation only. Crystal renderer is unavailable and fails closed.
/// </summary>
public static class LocalProductionReportEndpoints
{
    public static void MapLocalProductionReportEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/production-reports/catalog", (
            HttpContext httpContext,
            LocalApiSessionService sessions) =>
        {
            if (Guard(httpContext, sessions, ProductionReportPermissions.ReportsView) is IResult denied)
            {
                return denied;
            }

            return Results.Json(PreCrystalReportCatalog.All);
        });

        app.MapGet("/api/production-reports/renderer/status", (
            HttpContext httpContext,
            LocalApiSessionService sessions) =>
        {
            if (Guard(httpContext, sessions, ProductionReportPermissions.ReportsView) is IResult denied)
            {
                return denied;
            }

            UnavailableCrystalReportRenderer renderer = new();
            return Results.Json(new
            {
                status = renderer.GetStatus(),
                capability = renderer.GetCapability(),
                workerProtocol = CrystalWorkerProtocol.CurrentProtocolVersion,
                deploymentMode = CrystalWorkerProtocol.RequiredDeploymentMode,
                allowedOutputKinds = ProductionReportOutputKinds.All
            });
        });

        app.MapPost("/api/production-reports/dataset", async (
            ProductionReportDatasetApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildDatasetRequest(body, httpContext, sessions, out ProductionReportDatasetRequest request, out ProductionReportCatalogItem catalogItem, out IResult? bad))
            {
                return bad!;
            }

            Result<ProductionReportDatasetEnvelope> result = await ComposeDatasetService(connectionFactory)
                .BuildDatasetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return ToJson(result);
        });

        app.MapPost("/api/production-reports/render", async (
            ProductionReportRenderApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!ProductionReportOutputKinds.TryNormalize(body.RequestedOutputKind, out string requestedOutputKind, out Error? outputKindError))
            {
                return Results.Json(
                    new { code = outputKindError!.Code, message = outputKindError.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            ProductionReportDatasetApiRequest datasetBody = new(
                body.ReportKind,
                body.RangeStart,
                body.RangeEnd,
                body.BirMachineProfileId,
                body.RegistrationScopeCode,
                body.DocumentKind,
                body.TerminalId);

            if (!TryBuildDatasetRequest(datasetBody, httpContext, sessions, out ProductionReportDatasetRequest request, out ProductionReportCatalogItem catalogItem, out IResult? bad))
            {
                return bad!;
            }

            Result<ProductionReportDatasetEnvelope> dataset = await ComposeDatasetService(connectionFactory)
                .BuildDatasetAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (dataset.IsFailure)
            {
                return ToJson(dataset);
            }

            UnavailableCrystalReportRenderer renderer = new();
            Result<ProductionReportRenderResult> result = await renderer.RenderAsync(
                new ProductionReportRenderRequest(
                    dataset.Value,
                    requestedOutputKind,
                    body.TimeoutMilliseconds <= 0 ? 30000 : Math.Min(body.TimeoutMilliseconds, 120000),
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return ToJson(result);
        });
    }

    private static PreCrystalReportDatasetService ComposeDatasetService(SqliteConnectionFactory connectionFactory) =>
        new(new SqliteOfficialReportingProjectionService(connectionFactory));

    private static bool TryBuildDatasetRequest(
        ProductionReportDatasetApiRequest body,
        HttpContext httpContext,
        LocalApiSessionService sessions,
        out ProductionReportDatasetRequest request,
        out ProductionReportCatalogItem catalogItem,
        out IResult? error)
    {
        request = default!;
        catalogItem = default!;
        error = null;

        if (!PreCrystalReportCatalog.TryGet(body.ReportKind, out catalogItem))
        {
            error = Results.Json(
                new { code = "Reports.UnknownReportKind", message = "Report kind is not allow-listed." },
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (Guard(httpContext, sessions, catalogItem.RequiredPermission) is IResult denied)
        {
            error = denied;
            return false;
        }

        LocalApiSession? session = GetSession(httpContext, sessions);
        if (session is null)
        {
            error = Results.Json(
                new { code = "Session.Required", message = "Authenticated session required." },
                statusCode: StatusCodes.Status401Unauthorized);
            return false;
        }

        if (!TryParseRange(body, out DateTimeOffset start, out DateTimeOffset end, out IResult? badRange))
        {
            error = badRange;
            return false;
        }

        if (!TryParseMachineId(body.BirMachineProfileId, out Guid? machineId, out IResult? badMachine))
        {
            error = badMachine;
            return false;
        }

        if (!TryNormalizeDocumentKind(body.DocumentKind, out string normalizedDocumentKind, out IResult? badKind))
        {
            error = badKind;
            return false;
        }

        request = new ProductionReportDatasetRequest(
            catalogItem.Kind,
            start,
            end,
            machineId,
            string.IsNullOrWhiteSpace(body.RegistrationScopeCode) ? "LOCAL_SINGLE_STORE" : body.RegistrationScopeCode.Trim(),
            normalizedDocumentKind,
            session.UserId,
            string.IsNullOrWhiteSpace(body.TerminalId) ? null : body.TerminalId.Trim());
        return true;
    }

    private static IResult? Guard(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string requiredPermission)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        if (ApiAuthExtensions.RequireAuthenticatedSession(httpContext, sessions) is IResult unauthorized)
        {
            return unauthorized;
        }

        LocalApiSession? session = GetSession(httpContext, sessions);
        if (session is null)
        {
            return Results.Json(
                new { code = "Session.Required", message = "Authenticated session required." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!session.Permissions.Contains(requiredPermission, StringComparer.Ordinal))
        {
            return Results.Json(
                new { code = "Reports.Forbidden", message = "Current operator does not have permission for this report family." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static LocalApiSession? GetSession(HttpContext httpContext, LocalApiSessionService sessions) =>
        sessions.GetRequiredSession(httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName]);

    private static bool TryParseRange(
        ProductionReportDatasetApiRequest body,
        out DateTimeOffset start,
        out DateTimeOffset end,
        out IResult? error)
    {
        error = null;
        start = default;
        end = default;

        if (!DateTimeOffset.TryParse(body.RangeStart, out start) ||
            !DateTimeOffset.TryParse(body.RangeEnd, out end) ||
            end <= start)
        {
            error = Results.Json(
                new { code = "Reports.InvalidRange", message = "RangeStart/RangeEnd required and RangeEnd must be after RangeStart." },
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (end - start > ProductionReportDatasetLimits.MaxRange)
        {
            error = Results.Json(
                new { code = "Reports.RangeTooLarge", message = $"Report range exceeds the cap of {ProductionReportDatasetLimits.MaxRangeDays} days." },
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        return true;
    }

    private static bool TryParseMachineId(string? value, out Guid? machineId, out IResult? error)
    {
        machineId = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value.Trim(), out Guid parsed) || parsed == Guid.Empty)
        {
            error = Results.Json(
                new { code = "Reports.InvalidMachineProfileId", message = "BirMachineProfileId must be a valid non-empty GUID when supplied." },
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        machineId = parsed;
        return true;
    }

    private static bool TryNormalizeDocumentKind(string? value, out string normalized, out IResult? error)
    {
        normalized = "OFFICIAL_INVOICE";
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string candidate = value.Trim().ToUpperInvariant();
        if (string.Equals(candidate, "OFFICIAL_INVOICE", StringComparison.Ordinal) ||
            string.Equals(candidate, "OFFICIAL_RECEIPT", StringComparison.Ordinal))
        {
            normalized = candidate;
            return true;
        }

        error = Results.Json(
            new { code = "Reports.InvalidDocumentKind", message = "DocumentKind must be OFFICIAL_INVOICE or OFFICIAL_RECEIPT." },
            statusCode: StatusCodes.Status400BadRequest);
        return false;
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
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            };
            return Results.Json(
                new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                statusCode: code);
        }

        return Results.Json(result.Value);
    }

    public sealed record ProductionReportDatasetApiRequest(
        string? ReportKind,
        string? RangeStart,
        string? RangeEnd,
        string? BirMachineProfileId,
        string? RegistrationScopeCode,
        string? DocumentKind,
        string? TerminalId);

    public sealed record ProductionReportRenderApiRequest(
        string? ReportKind,
        string? RangeStart,
        string? RangeEnd,
        string? BirMachineProfileId,
        string? RegistrationScopeCode,
        string? DocumentKind,
        string? TerminalId,
        string? RequestedOutputKind,
        int TimeoutMilliseconds);
}
