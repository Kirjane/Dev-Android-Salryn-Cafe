using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Services.Reporting;
using Salryn.Core.Services.Reports;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Reports;

namespace Salryn.Api.Local;

/// <summary>
/// PRE-CRYSTAL M0 host-only production reporting spine.
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

            return Results.Json(new UnavailableCrystalReportRenderer().GetStatus());
        });

        app.MapPost("/api/production-reports/dataset", async (
            ProductionReportDatasetApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (!PreCrystalReportCatalog.TryGet(body.ReportKind, out ProductionReportCatalogItem catalogItem))
            {
                return Results.Json(
                    new { code = "Reports.UnknownReportKind", message = "Report kind is not allow-listed." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (Guard(httpContext, sessions, catalogItem.RequiredPermission) is IResult denied)
            {
                return denied;
            }

            LocalApiSession? session = GetSession(httpContext, sessions);
            if (session is null)
            {
                return Results.Json(
                    new { code = "Session.Required", message = "Authenticated session required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!TryParseRange(body, out DateTimeOffset start, out DateTimeOffset end, out IResult? badRange))
            {
                return badRange!;
            }

            Guid? machineId = Guid.TryParse(body.BirMachineProfileId, out Guid parsedMachineId)
                ? parsedMachineId
                : null;

            ProductionReportDatasetRequest request = new(
                body.ReportKind ?? string.Empty,
                start,
                end,
                machineId,
                body.RegistrationScopeCode,
                body.DocumentKind,
                session.UserId,
                body.TerminalId);

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
            if (!PreCrystalReportCatalog.TryGet(body.Dataset.ReportKind, out ProductionReportCatalogItem catalogItem))
            {
                return Results.Json(
                    new { code = "Reports.UnknownReportKind", message = "Report kind is not allow-listed." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (Guard(httpContext, sessions, catalogItem.RequiredPermission) is IResult denied)
            {
                return denied;
            }

            UnavailableCrystalReportRenderer renderer = new();
            Result<ProductionReportRenderResult> result = await renderer.RenderAsync(
                new ProductionReportRenderRequest(
                    body.Dataset,
                    string.IsNullOrWhiteSpace(body.RequestedOutputKind) ? "pdf" : body.RequestedOutputKind.Trim(),
                    body.TimeoutMilliseconds <= 0 ? 30000 : Math.Min(body.TimeoutMilliseconds, 120000),
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return ToJson(result);
        });
    }

    private static PreCrystalReportDatasetService ComposeDatasetService(SqliteConnectionFactory connectionFactory) =>
        new(new SqliteOfficialReportingProjectionService(connectionFactory));

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
        ProductionReportDatasetEnvelope Dataset,
        string? RequestedOutputKind,
        int TimeoutMilliseconds);
}
