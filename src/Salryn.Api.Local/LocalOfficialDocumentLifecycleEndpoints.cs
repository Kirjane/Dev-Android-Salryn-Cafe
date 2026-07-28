using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Core.Services.Lifecycle;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local official document lifecycle (M4-RC2 accounting).
/// Actor always from session. Amount-only refunds rejected.
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

            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    SalrynPermissions.OfficialReadingView) is IResult unauthorized)
            {
                return unauthorized;
            }

            return Results.Json(new OfficialDocumentLifecycleStatus(
                ProductionActivationEnabled: ProductionOfficialPostingActivation.IsEnabled,
                LiveOfficialPostingAllowed: false,
                ReprintImplemented: true,
                VoidRefundEventsImplemented: true,
                AdjustmentSeriesAllocationImplemented: false,
                Summary:
                "M4-RC2 host lifecycle accounting (engineering). Line-level refunds, tax reversals, settlements, " +
                "internal adjustment documents. Legal adjustment series not productized. Production activation OFF.",
                Notes: new[]
                {
                    "Original official documents remain immutable.",
                    "Refund requires explicit line quantities; amount-only ratio refunds are rejected.",
                    "Internal adjustment documents use INTERNAL_ADJUSTMENT_DOCUMENT (no legal series).",
                    "Actor is taken from authenticated session only.",
                    OfficialSafetyConstants.NoOfficialNumberIssued,
                    ProductionOfficialPostingActivation.NotAComplianceClaimNote
                }));
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

            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    PermissionFor(action)) is IResult permissionDenied)
            {
                return permissionDenied;
            }

            if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
            {
                return Results.Json(
                    new { code = "DocumentLifecycle.IdempotencyKeyRequired", message = "Idempotency key is required." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            Guid? journalId = Guid.TryParse(body.JournalEntryId, out Guid j) ? j : null;
            Guid? saleId = Guid.TryParse(body.SaleId, out Guid s) ? s : null;

            List<LifecycleRefundLineRequest>? refundLines = null;
            if (body.RefundLines is { Count: > 0 })
            {
                refundLines = new List<LifecycleRefundLineRequest>();
                foreach (RefundLineApiRequest line in body.RefundLines)
                {
                    if (!Guid.TryParse(line.SaleItemId, out Guid saleItemId))
                    {
                        return Results.Json(
                            new { code = "DocumentLifecycle.SaleItemRequired", message = "Each refund line needs saleItemId." },
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    if (!Enum.TryParse(line.Disposition ?? "Restock", ignoreCase: true, out StockReturnDisposition disposition))
                    {
                        return Results.Json(
                            new { code = "DocumentLifecycle.InvalidDisposition", message = "Unknown stock disposition." },
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    refundLines.Add(new LifecycleRefundLineRequest(
                        saleItemId,
                        line.QuantityX1000 ?? 0,
                        disposition,
                        line.OperatorNote,
                        line.ExpectedRefundAmountCents));
                }
            }

            LifecycleRefundSettlementRequest? settlement = null;
            if (body.Settlement is not null)
            {
                if (!Enum.TryParse(body.Settlement.Method ?? "Cash", ignoreCase: true, out RefundSettlementMethod method))
                {
                    return Results.Json(
                        new { code = "DocumentLifecycle.InvalidSettlementMethod", message = "Unknown settlement method." },
                        statusCode: StatusCodes.Status400BadRequest);
                }

                settlement = new LifecycleRefundSettlementRequest(
                    method,
                    body.Settlement.ExpectedAmountCents,
                    body.Settlement.ExternalReference,
                    body.Settlement.ImmediateComplete ?? true);
            }

            SqliteLifecycleAccountingService accounting = new(connectionFactory);
            Result<LifecycleAccountingResult> result = await accounting.ExecuteAsync(
                new LifecycleAccountingCommand(
                    action,
                    journalId,
                    body.OfficialDocumentNumber,
                    saleId,
                    body.Reason ?? string.Empty,
                    actorId,
                    body.TerminalId,
                    body.IdempotencyKey.Trim(),
                    refundLines,
                    settlement,
                    body.ExpectedTotalRefundCents,
                    body.FailureInjectionStage),
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

            LifecycleAccountingResult value = result.Value;
            return Results.Json(new
            {
                action = value.Action.ToString(),
                operationId = value.OperationId,
                eventId = value.EventId,
                journalEntryId = value.JournalEntryId,
                officialDocumentNumber = value.OfficialDocumentNumber,
                reason = value.Reason,
                actorUserId = value.ActorUserId,
                operationTimestamp = value.OperationTimestamp,
                sequenceAdvanced = value.SequenceAdvanced,
                originalImmutable = value.OriginalImmutable,
                lifecycleState = value.LifecycleState,
                stateVersion = value.StateVersion,
                summary = value.Summary,
                taxReversal = value.TaxReversal,
                refundLines = value.RefundLines,
                settlementId = value.SettlementId,
                settlementStatus = value.SettlementStatus?.ToString(),
                adjustmentDocumentId = value.AdjustmentDocumentId,
                adjustmentDocumentNumber = value.AdjustmentDocumentNumber,
                adjustmentSnapshotHash = value.AdjustmentSnapshotHash,
                reprintCanonicalSha256 = value.Output?.Manifest.CanonicalContentSha256,
                outputHtml = value.Output?.Artifacts.FirstOrDefault(a => a.Format == InvoiceOutputFormat.Html)?.Utf8Text
                    ?? value.Output?.Artifacts.FirstOrDefault()?.Utf8Text
            });
        });

        app.MapPost("/api/official/document-lifecycle/cancel-draft", async (
            DraftCancelApiRequest body,
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

            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    SalrynPermissions.OfficialDocumentCancel) is IResult permissionDenied)
            {
                return permissionDenied;
            }

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");
            if (!Guid.TryParse(userId, out Guid actorId))
            {
                return Results.Json(
                    new { code = "DocumentLifecycle.ActorRequired", message = "Authenticated actor required." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!Guid.TryParse(body.SaleId, out Guid saleId))
            {
                return Results.Json(
                    new { code = "DocumentLifecycle.SaleIdRequired", message = "Draft sale id required." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            SqliteLifecycleAccountingService accounting = new(connectionFactory);
            Result<LifecycleAccountingResult> result = await accounting.CancelDraftSaleAsync(
                saleId,
                actorId,
                body.Reason ?? "Draft cancel",
                body.IdempotencyKey ?? Guid.NewGuid().ToString("N"),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Results.Json(
                    new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Json(result.Value);
        });
    }

    private static string PermissionFor(OfficialDocumentLifecycleAction action) => action switch
    {
        OfficialDocumentLifecycleAction.Reprint => SalrynPermissions.OfficialDocumentReprint,
        OfficialDocumentLifecycleAction.Void => SalrynPermissions.OfficialDocumentVoid,
        OfficialDocumentLifecycleAction.Refund => SalrynPermissions.OfficialDocumentRefund,
        OfficialDocumentLifecycleAction.Cancel => SalrynPermissions.OfficialDocumentCancel,
        OfficialDocumentLifecycleAction.Adjust => SalrynPermissions.OfficialDocumentCorrection,
        _ => SalrynPermissions.SettingsManage
    };

    public sealed record LifecycleActionApiRequest(
        string? Action,
        string? JournalEntryId,
        string? OfficialDocumentNumber,
        string? SaleId,
        string? Reason,
        string? TerminalId,
        string? IdempotencyKey,
        long? ExpectedTotalRefundCents,
        IReadOnlyList<RefundLineApiRequest>? RefundLines,
        SettlementApiRequest? Settlement,
        string? FailureInjectionStage);

    public sealed record RefundLineApiRequest(
        string? SaleItemId,
        long? QuantityX1000,
        string? Disposition,
        string? OperatorNote,
        long? ExpectedRefundAmountCents);

    public sealed record SettlementApiRequest(
        string? Method,
        long? ExpectedAmountCents,
        string? ExternalReference,
        bool? ImmediateComplete);

    public sealed record DraftCancelApiRequest(
        string? SaleId,
        string? Reason,
        string? IdempotencyKey);
}
