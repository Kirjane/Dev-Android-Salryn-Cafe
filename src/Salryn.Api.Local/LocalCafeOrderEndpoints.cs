using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;

namespace Salryn.Api.Local;

internal static class LocalCafeOrderEndpoints
{
    public static TBuilder MapLocalCafeOrderEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/orders", (
            string? state,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersView) is IResult denied)
            {
                return denied;
            }

            return ToResult(
                composition.CafeOrders.List(state));
        });

        app.MapGet("/api/cafe/orders/{orderId:guid}", (
            Guid orderId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersView) is IResult denied)
            {
                return denied;
            }

            return ToResult(
                composition.CafeOrders.Find(orderId));
        });

        app.MapPost("/api/cafe/orders/revision", async (
            OrderRevisionRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            string permission =
                request.ExpectedVersion == 0
                    ? LocalApiPermissions.OrdersCreate
                    : LocalApiPermissions.OrdersManage;

            if (Require(
                    httpContext,
                    sessions,
                    permission) is IResult denied)
            {
                return denied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            CafeOrderRevisionMutation mutation = new(
                OperationId(request.OperationId),
                authority.Value.DeviceId,
                request.OrderId.GetValueOrDefault() == Guid.Empty
                    ? Guid.NewGuid()
                    : request.OrderId!.Value,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ??
                    DateTimeOffset.UtcNow,
                request.ServiceMode,
                request.ServiceReference,
                request.Lines.Select(line =>
                    new CafeOrderLineMutation(
                        line.OrderLineId.GetValueOrDefault() == Guid.Empty
                            ? Guid.NewGuid()
                            : line.OrderLineId!.Value,
                        line.ProductId,
                        line.VariantId,
                        line.ModifierOptionIds ??
                            Array.Empty<string>(),
                        line.QuantityX1000,
                        line.Note,
                        line.DisplayOrder))
                    .ToArray());

            return ToResult(
                composition.CafeOrders.SaveRevision(
                    mutation));
        });

        app.MapPost("/api/cafe/orders/{orderId:guid}/park", async (
            Guid orderId,
            OrderVersionRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            return ToResult(
                composition.CafeOrders.Park(
                    Lifecycle(
                        orderId,
                        request,
                        authority.Value)));
        });

        app.MapPost("/api/cafe/orders/{orderId:guid}/resume", async (
            Guid orderId,
            OrderVersionRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            return ToResult(
                composition.CafeOrders.Resume(
                    Lifecycle(
                        orderId,
                        request,
                        authority.Value)));
        });

        app.MapPost("/api/cafe/orders/{orderId:guid}/submit", async (
            Guid orderId,
            OrderVersionRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.PreparationManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            CafeOrderPreparationSubmitMutation mutation = new(
                OperationId(request.OperationId),
                authority.Value.DeviceId,
                orderId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ??
                    DateTimeOffset.UtcNow);

            return ToResult(
                composition.CafeOrders.SubmitPreparation(
                    mutation));
        });

        app.MapPost("/api/cafe/orders/{orderId:guid}/preparation", async (
            Guid orderId,
            PreparationStatusRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.PreparationManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            CafeOrderPreparationStatusMutation mutation = new(
                OperationId(request.OperationId),
                authority.Value.DeviceId,
                orderId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ??
                    DateTimeOffset.UtcNow,
                request.TargetState);

            return ToResult(
                composition.CafeOrders.SetPreparationStatus(
                    mutation));
        });

        app.MapPost("/api/cafe/orders/{orderId:guid}/cancel", async (
            Guid orderId,
            CancelOrderRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersCancel) is IResult denied)
            {
                return denied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            CafeOrderCancellationMutation mutation = new(
                OperationId(request.OperationId),
                authority.Value.DeviceId,
                orderId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ??
                    DateTimeOffset.UtcNow,
                request.Reason);

            return ToResult(
                composition.CafeOrders.Cancel(
                    mutation));
        });

        app.MapPost("/api/cafe/orders/{orderId:guid}/complete", async (
            Guid orderId,
            CompleteOrderRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.OrdersComplete) is IResult denied)
            {
                return denied;
            }

            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SalesCreate) is IResult saleDenied)
            {
                return saleDenied;
            }

            if ((request.Fees?.Count ?? 0) > 0 &&
                Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SalesFeeApply) is IResult feeDenied)
            {
                return feeDenied;
            }

            Result<LocalOrderAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authority.IsFailure)
            {
                return ToError(
                    authority.ErrorDetail);
            }

            List<CafeOrderPaymentMutation> payments = new();
            foreach (CompleteOrderPaymentRequest payment in
                request.Payments ?? Array.Empty<CompleteOrderPaymentRequest>())
            {
                if (!Enum.TryParse(
                        payment.Method,
                        true,
                        out PaymentMethod method))
                {
                    return Results.Json(
                        new
                        {
                            code = "CafeOrder.PaymentMethodInvalid",
                            error = "Payment method is invalid."
                        },
                        statusCode:
                            StatusCodes.Status400BadRequest);
                }

                payments.Add(
                    new CafeOrderPaymentMutation(
                        method,
                        payment.AmountCents));
            }

            List<CafeSaleFeeMutation> fees = new();
            foreach (CompleteOrderFeeRequest fee in
                request.Fees ?? Array.Empty<CompleteOrderFeeRequest>())
            {
                fees.Add(
                    new CafeSaleFeeMutation(
                        fee.FeeId.GetValueOrDefault() == Guid.Empty
                            ? Guid.NewGuid()
                            : fee.FeeId!.Value,
                        fee.FeeType,
                        fee.DisplayLabel,
                        fee.AmountCents,
                        fee.Reason));
            }

            CafeOrderCompletionMutation mutation = new(
                OperationId(
                    request.CompletionOperationId),
                OperationId(
                    request.SaleCommittedOperationId),
                authority.Value.DeviceId,
                orderId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ??
                    DateTimeOffset.UtcNow,
                payments)
            {
                Fees = fees
            };

            var guard = httpContext.RequestServices.GetRequiredService<CafeMutationIdempotencyGuard>();
            var result = composition.CafeOrderCompletion.Complete(mutation,
                (connection, transaction, committed) => guard.PersistAtomicOutcome(
                    httpContext, mutation.CompletionOperationId, connection, transaction,
                    new IdempotentExecutionOutcome(200, committed with { Idempotent = false })));
            return result.IsSuccess ? Results.Ok(result.Value with { Idempotent = false }) : ToError(result.ErrorDetail);
        }).WithMetadata(CafeMutationIdempotencyGuard.CanonicalCompletionMetadata);

        return app;
    }

    private static CafeOrderLifecycleMutation Lifecycle(
        Guid orderId,
        OrderVersionRequest request,
        LocalOrderAuthority authority) =>
        new(
            OperationId(request.OperationId),
            authority.DeviceId,
            orderId,
            authority.StoreId,
            request.ExpectedVersion,
            authority.ActorUserId,
            request.OccurredAtUtc ??
                DateTimeOffset.UtcNow);

    private static Guid OperationId(Guid? value) =>
        value.GetValueOrDefault();

    private static IResult? Require(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission) =>
        LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            permission);

    private static async Task<Result<LocalOrderAuthority>>
        ResolveAuthorityAsync(
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
            return Result<LocalOrderAuthority>.Fail(
                Error.Unauthorized(
                    "Auth.AuthenticationRequired",
                    "Authenticated local operator session is required."));
        }

        Result<DeploymentIdentitySnapshot?> deployment =
            await composition.DeploymentIdentityRepository
                .GetCurrentAsync(cancellationToken)
                .ConfigureAwait(false);

        if (deployment.IsFailure)
        {
            return Result<LocalOrderAuthority>.Fail(
                deployment.ErrorDetail);
        }

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalOrderAuthority>.Fail(
                Error.Conflict(
                    "CafeOrder.DeploymentIdentityMissing",
                    "Configured StoreId and local TerminalId are required."));
        }

        return Result<LocalOrderAuthority>.Success(
            new LocalOrderAuthority(
                deployment.Value.Store.StoreId,
                deployment.Value.Terminal.TerminalId,
                session.UserId));
    }

    private static IResult ToResult<T>(
        Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation =>
                StatusCodes.Status400BadRequest,
            ErrorType.NotFound =>
                StatusCodes.Status404NotFound,
            ErrorType.Conflict =>
                StatusCodes.Status409Conflict,
            ErrorType.Unauthorized =>
                StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden =>
                StatusCodes.Status403Forbidden,
            _ =>
                StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new
            {
                code = error.Code,
                error = error.Message
            },
            statusCode: statusCode);
    }

    private sealed record LocalOrderAuthority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId);

    private sealed record OrderRevisionRequest(
        Guid? OperationId,
        Guid? OrderId,
        int ExpectedVersion,
        string ServiceMode,
        string? ServiceReference,
        DateTimeOffset? OccurredAtUtc,
        IReadOnlyList<OrderLineRequest> Lines);

    private sealed record OrderLineRequest(
        Guid? OrderLineId,
        Guid ProductId,
        string? VariantId,
        IReadOnlyList<string>? ModifierOptionIds,
        long QuantityX1000,
        string? Note,
        int DisplayOrder);

    private sealed record OrderVersionRequest(
        Guid? OperationId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc);

    private sealed record PreparationStatusRequest(
        Guid? OperationId,
        int ExpectedVersion,
        string TargetState,
        DateTimeOffset? OccurredAtUtc);

    private sealed record CancelOrderRequest(
        Guid? OperationId,
        int ExpectedVersion,
        string Reason,
        DateTimeOffset? OccurredAtUtc);

    private sealed record CompleteOrderRequest(
        Guid? CompletionOperationId,
        Guid? SaleCommittedOperationId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc,
        IReadOnlyList<CompleteOrderPaymentRequest>? Payments,
        IReadOnlyList<CompleteOrderFeeRequest>? Fees);

    private sealed record CompleteOrderPaymentRequest(
        string Method,
        long AmountCents);

    private sealed record CompleteOrderFeeRequest(
        Guid? FeeId,
        string FeeType,
        string DisplayLabel,
        long AmountCents,
        string? Reason);
}
