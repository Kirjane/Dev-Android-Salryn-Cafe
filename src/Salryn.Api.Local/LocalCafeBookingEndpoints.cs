using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

internal static class LocalCafeBookingEndpoints
{
    public static TBuilder MapLocalCafeBookingEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/bookings", async (
            string? status,
            string? bookingMode,
            DateTimeOffset? eventStartUtc,
            DateTimeOffset? eventEndUtc,
            Guid? customerId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingView) is IResult denied)
            {
                return denied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeBooking.List(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        status,
                        bookingMode,
                        eventStartUtc,
                        eventEndUtc,
                        customerId));
        });

        app.MapGet("/api/cafe/bookings/customers", async (
            string? search,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingView) is IResult denied)
            {
                return denied;
            }

            SqliteAccountsReceivableService customers =
                new(composition.ConnectionFactory);
            Result<IReadOnlyList<ReceivableCustomerBalance>> result =
                await customers.SearchCustomersAsync(
                        search,
                        cancellationToken)
                    .ConfigureAwait(false);

            return ToResult(result);
        });

        app.MapGet("/api/cafe/bookings/{bookingId:guid}", async (
            Guid bookingId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingView) is IResult denied)
            {
                return denied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeBooking.Get(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        bookingId));
        });

        app.MapPost("/api/cafe/bookings", async (
            CreateBookingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            CafeBookingCreateMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                StableId(request.BookingId),
                StableId(request.RevisionId),
                authority.Value.DeviceId,
                authority.Value.StoreId,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                ToConfiguration(request.Configuration));

            return ToResult(
                composition.CafeBooking.Create(mutation));
        });

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/revision", async (
            Guid bookingId,
            ReviseBookingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            CafeBookingReviseMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                bookingId,
                StableId(request.RevisionId),
                authority.Value.DeviceId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                ToConfiguration(request.Configuration));

            return ToResult(
                composition.CafeBooking.Revise(mutation));
        });

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/confirm", async (
            Guid bookingId,
            AdvanceBookingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
            await AdvanceAsync(
                    bookingId,
                    request,
                    CafeBookingStatuses.Confirmed,
                    LocalApiPermissions.BookingManage,
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false));

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/ready-for-fulfillment", async (
            Guid bookingId,
            AdvanceBookingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
            await AdvanceAsync(
                    bookingId,
                    request,
                    CafeBookingStatuses.ReadyForFulfillment,
                    LocalApiPermissions.BookingManage,
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false));

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/cancel", async (
            Guid bookingId,
            CancelBookingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingCancel) is IResult denied)
            {
                return denied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafeBooking.Cancel(
                    new CafeBookingCancelMutation(
                        request.OperationId.GetValueOrDefault(),
                        bookingId,
                        authority.Value.DeviceId,
                        authority.Value.StoreId,
                        request.ExpectedVersion,
                        authority.Value.ActorUserId,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                        request.Reason)));
        });

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/deposits/receive", async (
            Guid bookingId,
            ReceiveBookingDepositRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingDepositReceive) is IResult denied)
            {
                return denied;
            }

            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.CashIn) is IResult cashDenied)
            {
                return cashDenied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Result<Guid> cashierSession =
                await ResolveCashierSessionAsync(
                        hostOperator,
                        authority.Value.ActorUserId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (cashierSession.IsFailure)
                return ToError(cashierSession.ErrorDetail);

            return ToResult(
                composition.CafeBooking.ReceiveDeposit(
                    new CafeBookingDepositReceiveMutation(
                        request.OperationId.GetValueOrDefault(),
                        StableId(request.DepositFactId),
                        StableId(request.CashMovementId),
                        authority.Value.DeviceId,
                        authority.Value.StoreId,
                        bookingId,
                        request.BookingRevisionId,
                        authority.Value.ActorUserId,
                        cashierSession.Value,
                        request.AmountCents,
                        request.Reference,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow)));
        });

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/deposits/refund", async (
            Guid bookingId,
            RefundBookingDepositRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingDepositRefund) is IResult denied)
            {
                return denied;
            }

            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.CashOut) is IResult cashDenied)
            {
                return cashDenied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Result<Guid> cashierSession =
                await ResolveCashierSessionAsync(
                        hostOperator,
                        authority.Value.ActorUserId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (cashierSession.IsFailure)
                return ToError(cashierSession.ErrorDetail);

            IReadOnlyList<CafeBookingDepositAllocationMutation> allocations =
                (request.Allocations ?? Array.Empty<RefundAllocationRequest>())
                    .Select(x => new CafeBookingDepositAllocationMutation(
                        x.ReceivedDepositFactId,
                        x.AmountCents))
                    .ToArray();

            return ToResult(
                composition.CafeBooking.RefundDeposit(
                    new CafeBookingDepositRefundMutation(
                        request.OperationId.GetValueOrDefault(),
                        StableId(request.DepositFactId),
                        StableId(request.CashMovementId),
                        authority.Value.DeviceId,
                        authority.Value.StoreId,
                        bookingId,
                        request.BookingRevisionId,
                        authority.Value.ActorUserId,
                        cashierSession.Value,
                        request.AmountCents,
                        request.Reason,
                        request.Reference,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                        allocations)));
        });

        app.MapPost("/api/cafe/bookings/{bookingId:guid}/fulfillment/start", async (
            Guid bookingId,
            StartBookingFulfillmentRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.BookingFulfill) is IResult denied)
            {
                return denied;
            }

            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersCreate) is IResult orderDenied)
            {
                return orderDenied;
            }

            Result<LocalBookingAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafeBooking.BindOrder(
                    new CafeBookingOrderBindMutation(
                        request.OperationId.GetValueOrDefault(),
                        bookingId,
                        StableId(request.OrderId),
                        authority.Value.DeviceId,
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        request.ExpectedVersion,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow)));
        });

        return app;
    }

    private static async Task<IResult> AdvanceAsync(
        Guid bookingId,
        AdvanceBookingRequest request,
        string targetStatus,
        string permission,
        HttpContext httpContext,
        LocalApiSessionService sessions,
        SalrynLocalComposition composition,
        CancellationToken cancellationToken)
    {
        if (Require(httpContext, sessions, permission) is IResult denied)
            return denied;

        Result<LocalBookingAuthority> authority =
            await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);

        if (authority.IsFailure)
            return ToError(authority.ErrorDetail);

        return ToResult(
            composition.CafeBooking.AdvanceStatus(
                new CafeBookingStatusMutation(
                    request.OperationId.GetValueOrDefault(),
                    bookingId,
                    authority.Value.DeviceId,
                    authority.Value.StoreId,
                    request.ExpectedVersion,
                    authority.Value.ActorUserId,
                    request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                    targetStatus)));
    }

    private static CafeBookingConfigurationMutation ToConfiguration(
        BookingConfigurationRequest request) =>
        new(
            request.CustomerId,
            request.BookingMode,
            request.EventName,
            request.EventReference,
            request.EventStartUtc,
            request.EventEndUtc,
            request.Location,
            request.SetupAtUtc,
            request.ServiceWindowStartUtc,
            request.ServiceWindowEndUtc,
            request.Headcount,
            request.PackageLabel,
            request.MenuPublicationRevisionId,
            request.DepositRequirementCents,
            request.SpecialInstructions,
            request.Notes,
            request.IntendedServiceMode,
            (request.Lines ?? Array.Empty<BookingLineRequest>())
                .Select(x => new CafeBookingLineMutation(
                    StableId(x.BookingLineId),
                    x.ProductId,
                    x.VariantId,
                    x.ModifierOptionIds ?? Array.Empty<string>(),
                    x.QuantityX1000,
                    x.LineNote,
                    x.DisplayOrder))
                .ToArray());

    private static Guid StableId(Guid? value) =>
        value.GetValueOrDefault() == Guid.Empty
            ? Guid.NewGuid()
            : value!.Value;

    private static IResult? Require(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission) =>
        LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            permission);

    private static async Task<Result<LocalBookingAuthority>> ResolveAuthorityAsync(
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
        {
            return Result<LocalBookingAuthority>.Fail(
                Error.Unauthorized(
                    "Auth.AuthenticationRequired",
                    "Authenticated local operator session is required."));
        }

        Result<DeploymentIdentitySnapshot?> deployment =
            await composition.DeploymentIdentityRepository
                .GetCurrentAsync(cancellationToken)
                .ConfigureAwait(false);

        if (deployment.IsFailure)
            return Result<LocalBookingAuthority>.Fail(deployment.ErrorDetail);

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalBookingAuthority>.Fail(
                Error.Conflict(
                    "CafeBooking.DeploymentIdentityMissing",
                    "Configured StoreId and local TerminalId are required."));
        }

        return Result<LocalBookingAuthority>.Success(
            new LocalBookingAuthority(
                deployment.Value.Store.StoreId,
                deployment.Value.Terminal.TerminalId,
                session.UserId));
    }

    private static async Task<Result<Guid>> ResolveCashierSessionAsync(
        HostOperatorApiServices hostOperator,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        HostOperatorShiftOperationResult result =
            await hostOperator.ShiftService
                .ReadShiftAsync(actorUserId, cancellationToken)
                .ConfigureAwait(false);

        if (!result.Ok)
        {
            return Result<Guid>.Fail(
                Error.Conflict(result.Code, result.Message));
        }

        string? shiftId = result.Shift?.ShiftId;
        if (result.Shift?.ShiftState != "Open" ||
            !Guid.TryParse(shiftId, out Guid cashierSessionId) ||
            cashierSessionId == Guid.Empty)
        {
            return Result<Guid>.Fail(
                Error.Conflict(
                    "CafeBooking.OpenCashierSessionRequired",
                    "An open cashier shift is required for Booking deposit cash operations."));
        }

        return Result<Guid>.Success(cashierSessionId);
    }

    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new { code = error.Code, error = error.Message },
            statusCode: statusCode);
    }

    private sealed record LocalBookingAuthority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId);

    private sealed record BookingLineRequest(
        Guid? BookingLineId,
        Guid ProductId,
        string? VariantId,
        IReadOnlyList<string>? ModifierOptionIds,
        long QuantityX1000,
        string? LineNote,
        int DisplayOrder);

    private sealed record BookingConfigurationRequest(
        Guid CustomerId,
        string BookingMode,
        string EventName,
        string? EventReference,
        DateTimeOffset EventStartUtc,
        DateTimeOffset? EventEndUtc,
        string? Location,
        DateTimeOffset? SetupAtUtc,
        DateTimeOffset? ServiceWindowStartUtc,
        DateTimeOffset? ServiceWindowEndUtc,
        int? Headcount,
        string? PackageLabel,
        string MenuPublicationRevisionId,
        long DepositRequirementCents,
        string? SpecialInstructions,
        string? Notes,
        string IntendedServiceMode,
        IReadOnlyList<BookingLineRequest>? Lines);

    private sealed record CreateBookingRequest(
        Guid? OperationId,
        Guid? BookingId,
        Guid? RevisionId,
        DateTimeOffset? OccurredAtUtc,
        BookingConfigurationRequest Configuration);

    private sealed record ReviseBookingRequest(
        Guid? OperationId,
        Guid? RevisionId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc,
        BookingConfigurationRequest Configuration);

    private sealed record AdvanceBookingRequest(
        Guid? OperationId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc);

    private sealed record CancelBookingRequest(
        Guid? OperationId,
        int ExpectedVersion,
        string Reason,
        DateTimeOffset? OccurredAtUtc);

    private sealed record ReceiveBookingDepositRequest(
        Guid? OperationId,
        Guid? DepositFactId,
        Guid? CashMovementId,
        Guid BookingRevisionId,
        long AmountCents,
        string Reference,
        DateTimeOffset? OccurredAtUtc);

    private sealed record RefundAllocationRequest(
        Guid ReceivedDepositFactId,
        long AmountCents);

    private sealed record RefundBookingDepositRequest(
        Guid? OperationId,
        Guid? DepositFactId,
        Guid? CashMovementId,
        Guid BookingRevisionId,
        long AmountCents,
        string Reason,
        string Reference,
        IReadOnlyList<RefundAllocationRequest>? Allocations,
        DateTimeOffset? OccurredAtUtc);

    private sealed record StartBookingFulfillmentRequest(
        Guid? OperationId,
        Guid? OrderId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc);
}
