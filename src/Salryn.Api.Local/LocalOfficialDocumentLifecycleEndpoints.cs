using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local official document lifecycle (reprint/void/refund).
/// M4-RC1: operational completion service (sale/stock/EJ/state/idempotency). Engineering only.
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

            Result<OfficialDocumentLifecycleStatus> status = BuildStatus();
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

            // Actor identity is always taken from the authenticated session — never from the body.
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

            if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
            {
                return Results.Json(
                    new { code = "DocumentLifecycle.IdempotencyKeyRequired", message = "Idempotency key is required." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            Guid? journalId = Guid.TryParse(body.JournalEntryId, out Guid j) ? j : null;
            Guid? saleId = Guid.TryParse(body.SaleId, out Guid s) ? s : null;

            SqliteOfficialDocumentLifecycleCompletionService completion =
                new(connectionFactory);

            Result<OfficialDocumentLifecycleActionResult> result = await completion.ExecuteAsync(
                new OfficialDocumentLifecycleRuntimeRequest(
                    action,
                    journalId,
                    body.OfficialDocumentNumber,
                    saleId,
                    body.Reason,
                    actorId,
                    body.TerminalId,
                    body.IdempotencyKey.Trim(),
                    body.RefundAmountCents),
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

    private static Result<OfficialDocumentLifecycleStatus> BuildStatus()
    {
        return Result<OfficialDocumentLifecycleStatus>.Success(new OfficialDocumentLifecycleStatus(
            ProductionActivationEnabled: ProductionOfficialPostingActivation.IsEnabled,
            LiveOfficialPostingAllowed: false,
            ReprintImplemented: true,
            VoidRefundEventsImplemented: true,
            AdjustmentSeriesAllocationImplemented: false,
            Summary:
            "M4-RC1 host official-document lifecycle completion (engineering). " +
            "Reprint/void/refund apply operational effects + append-only EJ events. " +
            "Production activation remains OFF.",
            Notes: new[]
            {
                "Original official documents remain immutable.",
                "Reprint renders REPRINT marker; does not allocate a new official sales number.",
                "Void/refund apply sale/stock effects and append lifecycle events in one transaction.",
                "Actor is taken from authenticated session only.",
                "Idempotency key is required for every lifecycle action.",
                "Adjustment series allocation is not productized in M4-RC1.",
                OfficialSafetyConstants.NoOfficialNumberIssued,
                ProductionOfficialPostingActivation.NotAComplianceClaimNote
            }));
    }

    public sealed record LifecycleActionApiRequest(
        string? Action,
        string? JournalEntryId,
        string? OfficialDocumentNumber,
        string? SaleId,
        string? Reason,
        string? TerminalId,
        string? IdempotencyKey,
        long? RefundAmountCents);
}
