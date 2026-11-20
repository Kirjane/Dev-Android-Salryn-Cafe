using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;

namespace Salryn.Api.Local;

internal static class LocalCafeAttendanceIncidentEndpoints
{
    public static TBuilder MapLocalCafeAttendanceIncidentEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/attendance", async (
            Guid? subjectUserId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.AttendanceView) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeAttendanceIncident.ListAttendance(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        subjectUserId,
                        fromUtc,
                        toUtc));
        });

        app.MapPost("/api/cafe/attendance", async (
            RecordAttendanceRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.AttendanceRecord) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Guid subject =
                request.SubjectUserId.GetValueOrDefault() == Guid.Empty
                    ? authority.Value.ActorUserId
                    : request.SubjectUserId!.Value;

            return ToResult(
                composition.CafeAttendanceIncident.RecordAttendance(
                    new(
                        request.OperationId.GetValueOrDefault(),
                        Stable(request.AttendanceEventId),
                        authority.Value.StoreId,
                        subject,
                        authority.Value.ActorUserId,
                        authority.Value.DeviceId,
                        request.EventType,
                        request.OccurredAtUtc ??
                            DateTimeOffset.UtcNow)));
        });

        app.MapPost("/api/cafe/attendance/{attendanceEventId:guid}/corrections", async (
            Guid attendanceEventId,
            CorrectAttendanceRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.AttendanceManage) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafeAttendanceIncident.CorrectAttendance(
                    new(
                        request.OperationId.GetValueOrDefault(),
                        Stable(request.AttendanceCorrectionId),
                        authority.Value.StoreId,
                        attendanceEventId,
                        authority.Value.ActorUserId,
                        authority.Value.DeviceId,
                        request.CorrectionKind,
                        request.Reason,
                        request.OccurredAtUtc ??
                            DateTimeOffset.UtcNow,
                        request.ReplacementAttendanceEventId,
                        request.ReplacementEventType,
                        request.ReplacementOccurredAtUtc)));
        });

        app.MapGet("/api/cafe/incidents", async (
            string? status,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.IncidentView) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeAttendanceIncident.ListIncidents(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        status));
        });

        app.MapGet("/api/cafe/incidents/{incidentId:guid}", async (
            Guid incidentId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.IncidentView) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeAttendanceIncident.GetIncident(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        incidentId));
        });

        app.MapPost("/api/cafe/incidents", async (
            CreateIncidentRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.IncidentRecord) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafeAttendanceIncident.CreateIncident(
                    new(
                        request.OperationId.GetValueOrDefault(),
                        Stable(request.IncidentId),
                        Stable(request.RevisionId),
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        authority.Value.DeviceId,
                        request.SubjectUserId,
                        request.IncidentOccurredAtUtc,
                        request.Title,
                        request.Description,
                        request.Classification,
                        request.Severity,
                        request.OccurredAtUtc ??
                            DateTimeOffset.UtcNow)));
        });

        app.MapPost("/api/cafe/incidents/{incidentId:guid}/revision", async (
            Guid incidentId,
            ReviseIncidentRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.IncidentManage) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafeAttendanceIncident.ReviseIncident(
                    new(
                        request.OperationId.GetValueOrDefault(),
                        incidentId,
                        Stable(request.RevisionId),
                        authority.Value.StoreId,
                        request.ExpectedVersion,
                        authority.Value.ActorUserId,
                        authority.Value.DeviceId,
                        request.Title,
                        request.Description,
                        request.Classification,
                        request.Severity,
                        request.OccurredAtUtc ??
                            DateTimeOffset.UtcNow)));
        });

        app.MapPost("/api/cafe/incidents/{incidentId:guid}/resolve", async (
            Guid incidentId,
            ResolveIncidentRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.IncidentManage) is IResult denied)
                return denied;

            Result<Authority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafeAttendanceIncident.ResolveIncident(
                    new(
                        request.OperationId.GetValueOrDefault(),
                        Stable(request.ResolutionId),
                        incidentId,
                        authority.Value.StoreId,
                        request.ExpectedVersion,
                        authority.Value.ActorUserId,
                        authority.Value.DeviceId,
                        request.ResolutionNote,
                        request.OccurredAtUtc ??
                            DateTimeOffset.UtcNow)));
        });

        return app;
    }

    private static async Task<Result<Authority>> ResolveAuthorityAsync(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        SalrynLocalComposition composition,
        CancellationToken cancellationToken)
    {
        LocalApiSession? session =
            sessions.GetRequiredSession(
                httpContext.Request.Cookies[
                    LocalApiSessionService.SessionCookieName]);

        if (session is null)
            return Result<Authority>.Fail(
                Error.Unauthorized(
                    "Auth.AuthenticationRequired",
                    "Authenticated local operator session is required."));

        Result<DeploymentIdentitySnapshot?> deployment =
            await composition.DeploymentIdentityRepository
                .GetCurrentAsync(cancellationToken)
                .ConfigureAwait(false);

        if (deployment.IsFailure)
            return Result<Authority>.Fail(
                deployment.ErrorDetail);

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
            return Result<Authority>.Fail(
                Error.Conflict(
                    "CafeAttendanceIncident.DeploymentIdentityMissing",
                    "Configured StoreId and TerminalId are required."));

        return Result<Authority>.Success(
            new(
                deployment.Value.Store.StoreId,
                deployment.Value.Terminal.TerminalId,
                session.UserId));
    }

    private static IResult? Require(
        HttpContext context,
        LocalApiSessionService sessions,
        string permission) =>
        LocalApiPermissionGuards.RequirePermission(
            context,
            sessions,
            permission);

    private static Guid Stable(Guid? value) =>
        value.GetValueOrDefault() == Guid.Empty
            ? Guid.NewGuid()
            : value!.Value;

    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int status = error.Type switch
        {
            ErrorType.Validation => 400,
            ErrorType.NotFound => 404,
            ErrorType.Conflict => 409,
            ErrorType.Unauthorized => 401,
            ErrorType.Forbidden => 403,
            _ => 500
        };

        return Results.Json(
            new { code = error.Code, error = error.Message },
            statusCode: status);
    }

    private sealed record Authority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId);

    private sealed record RecordAttendanceRequest(
        Guid? OperationId,
        Guid? AttendanceEventId,
        Guid? SubjectUserId,
        string EventType,
        DateTimeOffset? OccurredAtUtc);

    private sealed record CorrectAttendanceRequest(
        Guid? OperationId,
        Guid? AttendanceCorrectionId,
        string CorrectionKind,
        string Reason,
        DateTimeOffset? OccurredAtUtc,
        Guid? ReplacementAttendanceEventId,
        string? ReplacementEventType,
        DateTimeOffset? ReplacementOccurredAtUtc);

    private sealed record CreateIncidentRequest(
        Guid? OperationId,
        Guid? IncidentId,
        Guid? RevisionId,
        Guid? SubjectUserId,
        DateTimeOffset IncidentOccurredAtUtc,
        string Title,
        string Description,
        string Classification,
        string Severity,
        DateTimeOffset? OccurredAtUtc);

    private sealed record ReviseIncidentRequest(
        Guid? OperationId,
        Guid? RevisionId,
        int ExpectedVersion,
        string Title,
        string Description,
        string Classification,
        string Severity,
        DateTimeOffset? OccurredAtUtc);

    private sealed record ResolveIncidentRequest(
        Guid? OperationId,
        Guid? ResolutionId,
        int ExpectedVersion,
        string ResolutionNote,
        DateTimeOffset? OccurredAtUtc);
}
