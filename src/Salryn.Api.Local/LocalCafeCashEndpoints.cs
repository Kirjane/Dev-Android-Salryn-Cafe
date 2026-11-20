using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data;

namespace Salryn.Api.Local;

internal static class LocalCafeCashEndpoints
{
    public static TBuilder MapLocalCafeCashEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/cash/movements", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.CashView) is IResult denied)
            {
                return denied;
            }

            Result<LocalCashAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafeCash.GetSessionMovements(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        authority.Value.CashierSessionId));
        });

        app.MapPost("/api/cafe/cash/movements", async (
            CashPostRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseMovementType(
                    request.MovementType,
                    out CashMovementType movementType,
                    out string permission,
                    out string referenceType))
            {
                return Results.Json(
                    new
                    {
                        code = "CafeCash.MovementTypeInvalid",
                        error = "Movement type must be CashIn, CashOut, OperationalExpense, or InventoryPurchase."
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (Require(
                    httpContext,
                    sessions,
                    permission) is IResult denied)
            {
                return denied;
            }

            Result<LocalCashAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            Guid referenceId =
                movementType == CashMovementType.InventoryPurchase
                    ? request.ReferenceId.GetValueOrDefault()
                    : request.ReferenceId.GetValueOrDefault() == Guid.Empty
                        ? Guid.NewGuid()
                        : request.ReferenceId!.Value;

            if (movementType == CashMovementType.InventoryPurchase &&
                referenceId == Guid.Empty)
            {
                return Results.Json(
                    new
                    {
                        code = "CafeCash.InventoryPurchaseReferenceRequired",
                        error = "InventoryPurchase requires the accepted purchase receipt ID."
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            CafeCashPostMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                Id(request.MovementId),
                authority.Value.DeviceId,
                authority.Value.StoreId,
                authority.Value.ActorUserId,
                authority.Value.CashierSessionId,
                movementType,
                request.AmountCents,
                request.Reason,
                referenceType,
                referenceId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow);

            return ToResult(
                composition.CafeCash.Post(mutation));
        });

        app.MapPost("/api/cafe/cash/movements/{originalMovementId:guid}/reverse", async (
            Guid originalMovementId,
            CashReverseRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.CashReverse) is IResult denied)
            {
                return denied;
            }

            Result<LocalCashAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            CafeCashReversalMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                Id(request.MovementId),
                authority.Value.DeviceId,
                authority.Value.StoreId,
                authority.Value.ActorUserId,
                authority.Value.CashierSessionId,
                originalMovementId,
                request.Reason,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow);

            return ToResult(
                composition.CafeCash.Reverse(mutation));
        });

        return app;
    }

    private static bool TryParseMovementType(
        string? value,
        out CashMovementType movementType,
        out string permission,
        out string referenceType)
    {
        movementType = default;
        permission = string.Empty;
        referenceType = string.Empty;

        if (!Enum.TryParse(
                value,
                ignoreCase: true,
                out CashMovementType parsed))
        {
            return false;
        }

        switch (parsed)
        {
            case CashMovementType.CashIn:
                movementType = parsed;
                permission = LocalApiPermissions.CashIn;
                referenceType = CafeCashReferenceTypes.CashIn;
                return true;

            case CashMovementType.CashOut:
                movementType = parsed;
                permission = LocalApiPermissions.CashOut;
                referenceType = CafeCashReferenceTypes.CashOut;
                return true;

            case CashMovementType.OperationalExpense:
                movementType = parsed;
                permission = LocalApiPermissions.ExpensesRecord;
                referenceType = CafeCashReferenceTypes.OperationalExpense;
                return true;

            case CashMovementType.InventoryPurchase:
                movementType = parsed;
                permission = LocalApiPermissions.CashOut;
                referenceType = CafeCashReferenceTypes.InventoryPurchase;
                return true;

            default:
                return false;
        }
    }

    private static async Task<Result<LocalCashAuthority>>
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
            return Result<LocalCashAuthority>.Fail(
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
            return Result<LocalCashAuthority>.Fail(
                deployment.ErrorDetail);
        }

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalCashAuthority>.Fail(
                Error.Conflict(
                    "CafeCash.DeploymentIdentityMissing",
                    "Configured StoreId and local TerminalId are required."));
        }

        Guid? cashierSessionId =
            FindOpenCashierSession(
                composition.ConnectionFactory,
                session.UserId);

        if (!cashierSessionId.HasValue)
        {
            return Result<LocalCashAuthority>.Fail(
                Error.Conflict(
                    "CafeCash.OpenCashierSessionRequired",
                    "Open cashier session is required for C6 cash operations/history."));
        }

        return Result<LocalCashAuthority>.Success(
            new LocalCashAuthority(
                deployment.Value.Store.StoreId,
                deployment.Value.Terminal.TerminalId,
                session.UserId,
                cashierSessionId.Value));
    }

    private static Guid? FindOpenCashierSession(
        SqliteConnectionFactory factory,
        Guid actorUserId)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM cashier_sessions
            WHERE user_id=$user
              AND status=$open
            ORDER BY opened_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$user",
            actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$open",
            Convert.ToInt32(CashierSessionStatus.Open));

        object? value = command.ExecuteScalar();

        return value is string text &&
               Guid.TryParse(text, out Guid id)
            ? id
            : null;
    }

    private static Guid Id(Guid? value) =>
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

    private sealed record LocalCashAuthority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId,
        Guid CashierSessionId);

    private sealed record CashPostRequest(
        Guid? OperationId,
        Guid? MovementId,
        string MovementType,
        long AmountCents,
        string Reason,
        Guid? ReferenceId,
        DateTimeOffset? OccurredAtUtc);

    private sealed record CashReverseRequest(
        Guid? OperationId,
        Guid? MovementId,
        string Reason,
        DateTimeOffset? OccurredAtUtc);
}
