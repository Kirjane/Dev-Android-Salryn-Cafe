using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Api.Local;

internal static class LocalAccountsReceivableEndpoints
{
    public static WebApplication MapLocalAccountsReceivableEndpoints(this WebApplication app)
    {
        app.MapGet("/api/customers", async (string? search, HttpContext context, LocalApiSessionService sessions,
            IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesView) is IResult denied) return denied;
            return ToResult(await service.SearchCustomersAsync(search, ct));
        });

        app.MapPost("/api/customers", async (CreateCustomerApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesManageCustomers) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.CreateCustomerAsync(new(
                request.FullName, request.Phone, request.Address, request.Note,
                request.CreditLimitCents, session.UserId), ct), StatusCodes.Status201Created);
        });

        app.MapPatch("/api/customers/{id:guid}", async (Guid id, UpdateCustomerApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesManageCustomers) is IResult denied) return denied;
            return ToResult(await service.UpdateCustomerAsync(id, new(
                request.FullName, request.Phone, request.Address, request.Note,
                request.CreditLimitCents, request.IsActive), ct));
        });

        app.MapGet("/api/receivables/summary", async (HttpContext context, LocalApiSessionService sessions,
            IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesView) is IResult denied) return denied;
            return ToResult(await service.GetSummaryAsync(ct));
        });

        app.MapGet("/api/receivables/customers", async (string? search, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesView) is IResult denied) return denied;
            return ToResult(await service.SearchCustomersAsync(search, ct));
        });

        app.MapGet("/api/receivables/customers/{id:guid}/ledger", async (Guid id, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesView) is IResult denied) return denied;
            return ToResult(await service.GetLedgerAsync(id, ct));
        });

        app.MapPost("/api/receivables/collections", async (CollectionApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesCollect) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.CollectAsync(new(
                request.CustomerId, request.AmountCents, request.Method, request.Note, session.UserId,
                request.ClientRequestId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/receivables/adjustments", async (AdjustmentApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesAdjust) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.AdjustAsync(new(
                request.CustomerId, request.AmountCents, request.IsDebit, request.Note, session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/receivables/collections/{id:guid}/void", async (Guid id, VoidCollectionApiRequest request,
            HttpContext context, LocalApiSessionService sessions, IAccountsReceivableService service,
            CancellationToken ct) =>
        {
            if (Guard(context, sessions, LocalApiPermissions.ReceivablesAdjust) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            Result result = await service.VoidCollectionAsync(id, session.UserId, request.Reason, ct);
            return result.IsSuccess ? Results.Ok(new { status = "Voided" }) : ToError(result.ErrorDetail);
        });

        return app;
    }

    private static IResult? Guard(HttpContext context, LocalApiSessionService sessions, string permission) =>
        LocalApiPermissionGuards.RequirePermission(context, sessions, permission);

    private static LocalApiSession RequiredSession(HttpContext context, LocalApiSessionService sessions) =>
        sessions.GetRequiredSession(context.Request.Cookies[LocalApiSessionService.SessionCookieName])!;

    private static IResult ToResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new { error = error.Message, code = error.Code }, statusCode: status);
    }

    private sealed record CreateCustomerApiRequest(
        string FullName, string? Phone, string? Address, string? Note, long? CreditLimitCents);
    private sealed record UpdateCustomerApiRequest(
        string FullName, string? Phone, string? Address, string? Note, long? CreditLimitCents, bool IsActive);
    private sealed record CollectionApiRequest(
        Guid CustomerId,
        long AmountCents,
        string Method,
        string? Note,
        string ClientRequestId);
    private sealed record AdjustmentApiRequest(Guid CustomerId, long AmountCents, bool IsDebit, string Note);
    private sealed record VoidCollectionApiRequest(string Reason);
}
