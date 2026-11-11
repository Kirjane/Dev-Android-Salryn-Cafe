using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;

namespace Salryn.Api.Local;

internal static class LocalAccountsReceivableEndpoints
{
    public static WebApplication MapLocalAccountsReceivableEndpoints(this WebApplication app)
    {
        app.MapGet("/api/customers", async (string? search, HttpContext context, LocalApiSessionService sessions,
            IAccountsReceivableService service, ILocalLicenseEnforcementService licensing, CancellationToken ct) =>
        {
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesView, ct) is IResult denied) return denied;
            return ToResult(await service.SearchCustomersAsync(search, ct));
        });

        app.MapPost("/api/customers", async (CreateCustomerApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesManageCustomers, ct) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.CreateCustomerAsync(new(
                request.FullName, request.Phone, request.Address, request.Note,
                request.CreditLimitCents, session.UserId), ct), StatusCodes.Status201Created);
        });

        app.MapPatch("/api/customers/{id:guid}", async (Guid id, UpdateCustomerApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesManageCustomers, ct) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.UpdateCustomerAsync(id, new(
                request.FullName, request.Phone, request.Address, request.Note,
                request.CreditLimitCents, request.IsActive), ct, session.UserId));
        });

        app.MapGet("/api/receivables/summary", async (HttpContext context, LocalApiSessionService sessions,
            IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesView, ct) is IResult denied) return denied;
            return ToResult(await service.GetSummaryAsync(ct));
        });

        app.MapGet("/api/receivables/customers", async (string? search, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesView, ct) is IResult denied) return denied;
            return ToResult(await service.SearchCustomersAsync(search, ct));
        });

        app.MapGet("/api/receivables/customers/page", async (string? search,int? page,int? pageSize,HttpContext context,
            LocalApiSessionService sessions,IAccountsReceivableService service,CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing=context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if(await Guard(context,sessions,licensing,LocalApiPermissions.ReceivablesView,ct) is IResult denied)return denied;
            int number=Math.Max(1,page??1),size=Math.Clamp(pageSize??50,1,100);
            Result<ReceivablePage<ReceivableCustomerBalance>> result=await service.SearchCustomersPageAsync(search,(number-1)*size,size,ct);
            return result.IsFailure?ToError(result.ErrorDetail):Results.Ok(new{items=result.Value.Items,pageNumber=number,pageSize=size,totalCount=result.Value.Total,totalPages=(int)Math.Ceiling(result.Value.Total/(double)size),hasMore=result.Value.HasMore,result.Value.AsOfUtc,result.Value.TimeZoneId});
        });

        app.MapGet("/api/receivables/customers/{id:guid}/ledger", async (Guid id, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesView, ct) is IResult denied) return denied;
            return ToResult(await service.GetLedgerAsync(id, ct));
        });

        app.MapGet("/api/receivables/customers/{id:guid}/ledger/page", async (Guid id,int? page,int? pageSize,HttpContext context,
            LocalApiSessionService sessions,IAccountsReceivableService service,CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing=context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if(await Guard(context,sessions,licensing,LocalApiPermissions.ReceivablesView,ct) is IResult denied)return denied;
            int number=Math.Max(1,page??1),size=Math.Clamp(pageSize??100,1,200);
            Result<ReceivablePage<AccountsReceivableEntry>> result=await service.GetLedgerPageAsync(id,(number-1)*size,size,ct);
            return result.IsFailure?ToError(result.ErrorDetail):Results.Ok(new{items=result.Value.Items,pageNumber=number,pageSize=size,totalCount=result.Value.Total,totalPages=(int)Math.Ceiling(result.Value.Total/(double)size),hasMore=result.Value.HasMore,result.Value.AsOfUtc,result.Value.TimeZoneId});
        });

        app.MapPost("/api/receivables/collections", async (CollectionApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesCollect, ct) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            Result<AccountsReceivableCollection> collection = await service.CollectAsync(new(
                request.CustomerId, request.AmountCents, request.Method, request.Note, session.UserId,
                request.ClientRequestId), ct);
            if (collection.IsFailure) return ToError(collection.ErrorDetail);
            Result<CollectionPaymentAcknowledgment> acknowledgment =
                await service.GetCollectionAcknowledgmentAsync(collection.Value.Id, ct);
            if (acknowledgment.IsFailure) return ToError(acknowledgment.ErrorDetail);
            return Results.Json(
                new CollectionApiResponse(
                    collection.Value,
                    acknowledgment.Value,
                    RenderCollectionAcknowledgment(acknowledgment.Value)),
                statusCode: StatusCodes.Status201Created);
        });

        app.MapPost("/api/receivables/adjustments", async (AdjustmentApiRequest request, HttpContext context,
            LocalApiSessionService sessions, IAccountsReceivableService service, CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesAdjust, ct) is IResult denied) return denied;
            _ = request;
            _ = service;
            return Results.NotFound(new
            {
                code = "Receivables.AdjustmentsUnavailable",
                message = "Manual receivable adjustments are not available in this release."
            });
        });

        app.MapPost("/api/receivables/collections/{id:guid}/void", async (Guid id, VoidCollectionApiRequest request,
            HttpContext context, LocalApiSessionService sessions, IAccountsReceivableService service,
            CancellationToken ct) =>
        {
            ILocalLicenseEnforcementService licensing = context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await Guard(context, sessions, licensing, LocalApiPermissions.ReceivablesAdjust, ct) is IResult denied) return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            Result result = await service.VoidCollectionAsync(id, session.UserId, request.Reason, ct);
            return result.IsSuccess ? Results.Ok(new { status = "Voided" }) : ToError(result.ErrorDetail);
        });

        return app;
    }

    private static async Task<IResult?> Guard(
        HttpContext context,
        LocalApiSessionService sessions,
        ILocalLicenseEnforcementService licensing,
        string permission,
        CancellationToken cancellationToken)
    {
        if (ApiAuthExtensions.RequireAuthenticatedSession(context, sessions) is IResult unauthenticated)
        {
            return unauthenticated;
        }

        if (await ApiLicenseGuard.RequireLicensedFeatureAsync(
                licensing,
                LicenseFeatures.ReceivablesUtangLedger,
                cancellationToken).ConfigureAwait(false) is IResult unlicensed)
        {
            return unlicensed;
        }

        return LocalApiPermissionGuards.RequirePermission(context, sessions, permission);
    }

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

    private static string RenderCollectionAcknowledgment(CollectionPaymentAcknowledgment acknowledgment) =>
        $"""
        COLLECTION RECEIPT
        Operational payment acknowledgment

        Customer: {acknowledgment.CustomerName}
        Amount paid: {acknowledgment.AmountPaidCents / 100m:N2}
        Previous balance: {acknowledgment.PreviousBalanceCents / 100m:N2}
        New balance: {acknowledgment.NewBalanceCents / 100m:N2}
        Collected by: {acknowledgment.CollectedBy}
        Date/time: {acknowledgment.CollectedAt:yyyy-MM-dd HH:mm:ss zzz}
        Method: {acknowledgment.Method}
        Note/reference: {acknowledgment.Note ?? "None"}

        Store operational acknowledgment only.
        """;

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
    private sealed record CollectionApiResponse(
        AccountsReceivableCollection Collection,
        CollectionPaymentAcknowledgment Acknowledgment,
        string PlainTextAcknowledgment);
    private sealed record AdjustmentApiRequest(Guid CustomerId, long AmountCents, bool IsDebit, string Note);
    private sealed record VoidCollectionApiRequest(string Reason);
}
