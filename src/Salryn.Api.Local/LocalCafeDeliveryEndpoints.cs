using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;

namespace Salryn.Api.Local;

internal static class LocalCafeDeliveryEndpoints
{
    public static TBuilder MapLocalCafeDeliveryEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/deliveries", async (
            string? status,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryView) is IResult denied)
            {
                return denied;
            }

            Result<LocalDeliveryAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeDelivery.List(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        status));
        });

        app.MapGet("/api/cafe/deliveries/{deliveryId:guid}", async (
            Guid deliveryId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryView) is IResult denied)
            {
                return denied;
            }

            Result<LocalDeliveryAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeDelivery.Get(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        deliveryId));
        });

        app.MapGet("/api/cafe/orders/{orderId:guid}/delivery", async (
            Guid orderId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryView) is IResult denied)
            {
                return denied;
            }

            Result<LocalDeliveryAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeDelivery.GetByOrder(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        orderId));
        });

        app.MapPost("/api/cafe/deliveries", async (
            CreateDeliveryRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalDeliveryAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            CafeDeliveryCreateMutation mutation = new(
                OperationId(request.OperationId),
                authority.Value.DeviceId,
                DeliveryId(request.DeliveryId),
                request.OrderId,
                authority.Value.StoreId,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                request.RecipientName,
                request.ContactPhone,
                request.DeliveryAddress,
                request.Landmark,
                request.Instructions,
                request.DeliveryFeeCents,
                request.CourierReference);

            return ToResult(
                composition.CafeDelivery.Create(mutation));
        });

        app.MapPost("/api/cafe/deliveries/{deliveryId:guid}/revision", async (
            Guid deliveryId,
            ReviseDeliveryRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalDeliveryAuthority> authority =
                await ResolveAuthorityAsync(
                        httpContext,
                        sessions,
                        composition,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            CafeDeliveryReviseMutation mutation = new(
                OperationId(request.OperationId),
                authority.Value.DeviceId,
                deliveryId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                request.RecipientName,
                request.ContactPhone,
                request.DeliveryAddress,
                request.Landmark,
                request.Instructions,
                request.DeliveryFeeCents,
                request.CourierReference);

            return ToResult(
                composition.CafeDelivery.Revise(mutation));
        });

        app.MapPost("/api/cafe/deliveries/{deliveryId:guid}/dispatch", async (
            Guid deliveryId,
            AdvanceDeliveryRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryAdvance) is IResult denied)
            {
                return denied;
            }

            return await AdvanceAsync(
                    deliveryId,
                    request.OperationId,
                    request.ExpectedVersion,
                    CafeDeliveryStatuses.Dispatched,
                    reason: null,
                    request.OccurredAtUtc,
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
        });

        app.MapPost("/api/cafe/deliveries/{deliveryId:guid}/resolve", async (
            Guid deliveryId,
            ResolveDeliveryRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryAdvance) is IResult denied)
            {
                return denied;
            }

            if (request.TargetStatus is not (
                    CafeDeliveryStatuses.Delivered or
                    CafeDeliveryStatuses.Failed))
            {
                return Results.Json(
                    new
                    {
                        code = "CafeDelivery.ResolveStatusInvalid",
                        error = "Resolve accepts only Delivered or Failed."
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return await AdvanceAsync(
                    deliveryId,
                    request.OperationId,
                    request.ExpectedVersion,
                    request.TargetStatus,
                    request.Reason,
                    request.OccurredAtUtc,
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
        });

        app.MapPost("/api/cafe/deliveries/{deliveryId:guid}/cancel", async (
            Guid deliveryId,
            CancelDeliveryRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.DeliveryManage) is IResult denied)
            {
                return denied;
            }

            return await AdvanceAsync(
                    deliveryId,
                    request.OperationId,
                    request.ExpectedVersion,
                    CafeDeliveryStatuses.Cancelled,
                    request.Reason,
                    request.OccurredAtUtc,
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);
        });

        return app;
    }

    private static async Task<IResult> AdvanceAsync(
        Guid deliveryId,
        Guid? operationId,
        int expectedVersion,
        string targetStatus,
        string? reason,
        DateTimeOffset? occurredAtUtc,
        HttpContext httpContext,
        LocalApiSessionService sessions,
        SalrynLocalComposition composition,
        CancellationToken cancellationToken)
    {
        Result<LocalDeliveryAuthority> authority =
            await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken)
                .ConfigureAwait(false);

        if (authority.IsFailure)
            return ToError(authority.ErrorDetail);

        CafeDeliveryAdvanceMutation mutation = new(
            OperationId(operationId),
            authority.Value.DeviceId,
            deliveryId,
            authority.Value.StoreId,
            expectedVersion,
            authority.Value.ActorUserId,
            occurredAtUtc ?? DateTimeOffset.UtcNow,
            targetStatus,
            reason);

        return ToResult(
            composition.CafeDelivery.Advance(mutation));
    }

    private static Guid OperationId(Guid? value) =>
        value.GetValueOrDefault();

    private static Guid DeliveryId(Guid? value) =>
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

    private static async Task<Result<LocalDeliveryAuthority>>
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
            return Result<LocalDeliveryAuthority>.Fail(
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
            return Result<LocalDeliveryAuthority>.Fail(
                deployment.ErrorDetail);
        }

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalDeliveryAuthority>.Fail(
                Error.Conflict(
                    "CafeDelivery.DeploymentIdentityMissing",
                    "Configured StoreId and local TerminalId are required."));
        }

        return Result<LocalDeliveryAuthority>.Success(
            new LocalDeliveryAuthority(
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

    private sealed record LocalDeliveryAuthority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId);

    private sealed record CreateDeliveryRequest(
        Guid? OperationId,
        Guid? DeliveryId,
        Guid OrderId,
        DateTimeOffset? OccurredAtUtc,
        string RecipientName,
        string? ContactPhone,
        string DeliveryAddress,
        string? Landmark,
        string? Instructions,
        long DeliveryFeeCents,
        string? CourierReference);

    private sealed record ReviseDeliveryRequest(
        Guid? OperationId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc,
        string RecipientName,
        string? ContactPhone,
        string DeliveryAddress,
        string? Landmark,
        string? Instructions,
        long DeliveryFeeCents,
        string? CourierReference);

    private sealed record AdvanceDeliveryRequest(
        Guid? OperationId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc);

    private sealed record ResolveDeliveryRequest(
        Guid? OperationId,
        int ExpectedVersion,
        string TargetStatus,
        string? Reason,
        DateTimeOffset? OccurredAtUtc);

    private sealed record CancelDeliveryRequest(
        Guid? OperationId,
        int ExpectedVersion,
        string Reason,
        DateTimeOffset? OccurredAtUtc);
}
