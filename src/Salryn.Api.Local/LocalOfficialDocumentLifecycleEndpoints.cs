using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local official document lifecycle (reprint/void/refund events). Engineering only.
/// </summary>
public static class LocalOfficialDocumentLifecycleEndpoints
{
    public static void MapLocalOfficialDocumentLifecycleEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/document-lifecycle/status", (
            HttpContext httpContext,
            LocalApiSessionService sessions) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            if (ApiAuthExtensions.RequireAuthenticatedSession(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            IHostOfficialDocumentLifecycleService service = Compose(connectionFactory);
            Result<OfficialDocumentLifecycleStatus> status = service.GetStatus();
            return Results.Json(status.Value);
        });

        app.MapPost("/api/official/document-lifecycle/action", async (
            LifecycleActionApiRequest body,
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
            if (!Guid.TryParse(userId, out Guid actorId))
            {
                return Results.Json(
                    new { code = "DocumentLifecycle.ActorRequired", message = "Authenticated actor required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!Enum.TryParse(body.Action, ignoreCase: true, out OfficialDocumentLifecycleAction action))
            {
                return Results.Json(
                    new { code = "DocumentLifecycle.UnknownAction", message = "Unknown lifecycle action." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            Guid? journalId = Guid.TryParse(body.JournalEntryId, out Guid j) ? j : null;
            Guid? saleId = Guid.TryParse(body.SaleId, out Guid s) ? s : null;

            IHostOfficialDocumentLifecycleService service = Compose(connectionFactory);
            Result<OfficialDocumentLifecycleActionResult> result = await service.ExecuteAsync(
                new OfficialDocumentLifecycleActionRequest(
                    action,
                    journalId,
                    body.OfficialDocumentNumber,
                    saleId,
                    body.Reason ?? string.Empty,
                    actorId,
                    body.TerminalId),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                int code = result.ErrorDetail.Type switch
                {
                    ErrorType.Validation => StatusCodes.Status400BadRequest,
                    ErrorType.NotFound => StatusCodes.Status404NotFound,
                    ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                    ErrorType.Conflict => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status500InternalServerError
                };
                return Results.Json(
                    new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                    statusCode: code);
            }

            OfficialDocumentLifecycleActionResult value = result.Value;
            return Results.Json(new
            {
                action = value.Action.ToString(),
                eventId = value.EventId,
                journalEntryId = value.JournalEntryId,
                officialDocumentNumber = value.OfficialDocumentNumber,
                reason = value.Reason,
                actorUserId = value.ActorUserId,
                createdAt = value.CreatedAt,
                sequenceAdvanced = value.SequenceAdvanced,
                originalImmutable = value.OriginalImmutable,
                summary = value.Summary,
                reprintCanonicalSha256 = value.ReprintOutput?.Manifest.CanonicalContentSha256
            });
        });
    }

    private static IHostOfficialDocumentLifecycleService Compose(SqliteConnectionFactory factory)
    {
        SqliteOfficialDocumentLifecycleReadService lifecycleRead = new(factory);
        SqliteOfficialDocumentReprintService reprintPersistence = new(factory);
        OfficialDocumentReprintService reprint = new(lifecycleRead, reprintPersistence);
        SqliteOfficialDocumentLifecycleMutationService mutation = new(factory);
        InvoiceOutputService invoiceOutput = new(new SqliteOfficialInvoiceSnapshotReadService(factory));
        return new HostOfficialDocumentLifecycleService(reprint, mutation, invoiceOutput);
    }

    public sealed record LifecycleActionApiRequest(
        string? Action,
        string? JournalEntryId,
        string? OfficialDocumentNumber,
        string? SaleId,
        string? Reason,
        string? TerminalId);
}
