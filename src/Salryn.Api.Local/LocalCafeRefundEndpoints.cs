using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data;

namespace Salryn.Api.Local;

internal static class LocalCafeRefundEndpoints
{
    public static TBuilder MapLocalCafeRefundEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/refunds", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (RequireAny(httpContext, sessions, LocalApiPermissions.SalesRefundRequest, LocalApiPermissions.SalesRefundApprove) is IResult denied)
                return denied;

            Result<LocalRefundAuthority> authority = await ResolveAuthorityAsync(httpContext, sessions, composition, cancellationToken).ConfigureAwait(false);
            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return Results.Ok(ReadStoreRefunds(composition.ConnectionFactory, authority.Value.StoreId));
        });

        app.MapGet("/api/cafe/refunds/sales", async (
            string? search,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (RequireAny(httpContext, sessions, LocalApiPermissions.SalesRefundRequest, LocalApiPermissions.SalesRefundApprove) is IResult denied)
                return denied;

            Result<LocalRefundAuthority> authority = await ResolveAuthorityAsync(httpContext, sessions, composition, cancellationToken).ConfigureAwait(false);
            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return Results.Ok(ReadRefundableSales(composition.ConnectionFactory, authority.Value.StoreId, search));
        });

        app.MapGet("/api/cafe/refunds/{refundId:guid}", async (
            Guid refundId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (RequireAny(httpContext, sessions, LocalApiPermissions.SalesRefundRequest, LocalApiPermissions.SalesRefundApprove) is IResult denied)
                return denied;

            Result<LocalRefundAuthority> authority = await ResolveAuthorityAsync(httpContext, sessions, composition, cancellationToken).ConfigureAwait(false);
            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Result<CafeRefundDetail> detail = composition.CafeRefunds.Get(refundId);
            if (detail.IsFailure)
                return ToError(detail.ErrorDetail);

            if (detail.Value.Refund.StoreId != authority.Value.StoreId)
            {
                return ToError(Error.Conflict("CafeRefund.StoreMismatch", "Refund belongs to another store."));
            }

            return Results.Ok(detail.Value);
        });

        app.MapPost("/api/cafe/refunds", async (
            RefundCreateRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(httpContext, sessions, LocalApiPermissions.SalesRefundRequest) is IResult denied)
                return denied;

            Result<LocalRefundAuthority> authority = await ResolveAuthorityAsync(httpContext, sessions, composition, cancellationToken).ConfigureAwait(false);
            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Guid operationId = request.OperationId.GetValueOrDefault();
            Guid refundId = request.RefundId.GetValueOrDefault() == Guid.Empty
                ? DeriveDeterministicGuid($"cafe-refund|{operationId:D}|{request.SaleId:D}")
                : request.RefundId!.Value;

            IReadOnlyList<CafeRefundLineMutation> lines = (request.Lines ?? Array.Empty<RefundLineRequest>())
                .Select((line, index) => new CafeRefundLineMutation(
                    line.RefundLineId.GetValueOrDefault() == Guid.Empty
                        ? DeriveDeterministicGuid($"cafe-refund-line|{operationId:D}|{index}|{line.SaleItemId:D}")
                        : line.RefundLineId!.Value,
                    line.SaleItemId,
                    line.QuantityX1000,
                    line.StockDisposition,
                    line.OperatorNote))
                .ToArray();

            Result<CafeRefundMutationResult> result = composition.CafeRefunds.Request(new CafeRefundRequestMutation(
                operationId,
                authority.Value.DeviceId,
                refundId,
                request.SaleId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                request.Reason,
                lines));

            return ToResult(result);
        });

        app.MapPost("/api/cafe/refunds/{refundId:guid}/resolve", async (
            Guid refundId,
            RefundResolveRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(httpContext, sessions, LocalApiPermissions.SalesRefundApprove) is IResult denied)
                return denied;

            Result<LocalRefundAuthority> authority = await ResolveAuthorityAsync(httpContext, sessions, composition, cancellationToken).ConfigureAwait(false);
            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Result<CafeRefundMutationResult> result = composition.CafeRefunds.Resolve(new CafeRefundResolutionMutation(
                request.OperationId.GetValueOrDefault(),
                authority.Value.DeviceId,
                refundId,
                request.SaleId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                request.Approve,
                request.Reason));

            return ToResult(result);
        });

        app.MapPost("/api/cafe/refunds/{refundId:guid}/commit", async (
            Guid refundId,
            RefundCommitRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(httpContext, sessions, LocalApiPermissions.SalesRefundApprove) is IResult denied)
                return denied;

            Result<LocalRefundAuthority> authority = await ResolveAuthorityAsync(httpContext, sessions, composition, cancellationToken).ConfigureAwait(false);
            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Guid? cashierSessionId = FindOpenCashierSession(composition.ConnectionFactory, authority.Value.ActorUserId);
            if (!cashierSessionId.HasValue)
            {
                return ToError(Error.Conflict(
                    "CafeRefund.OpenCashierSessionRequired",
                    "An open cashier shift session is required to commit a refund."));
            }

            Guid operationId = request.OperationId.GetValueOrDefault();
            IReadOnlyList<CafeRefundSettlementMutation> settlements = (request.Settlements ?? Array.Empty<RefundSettlementRequest>())
                .Select((s, index) => new CafeRefundSettlementMutation(
                    s.SettlementPartId.GetValueOrDefault() == Guid.Empty
                        ? DeriveDeterministicGuid($"cafe-refund-settlement|{operationId:D}|{index}|{s.Method.Trim()}")
                        : s.SettlementPartId!.Value,
                    s.Method,
                    s.AmountCents))
                .ToArray();

            Result<CafeRefundCommitResult> result = composition.CafeRefunds.Commit(new CafeRefundCommitMutation(
                operationId,
                authority.Value.DeviceId,
                refundId,
                request.SaleId,
                authority.Value.StoreId,
                request.ExpectedVersion,
                authority.Value.ActorUserId,
                cashierSessionId.Value,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                settlements));

            return ToResult(result);
        });

        return app;
    }

    private static IReadOnlyList<object> ReadStoreRefunds(SqliteConnectionFactory factory, Guid storeId)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.refund_id, r.sale_id, s.receipt_no, r.current_version, r.state, r.reason,
                   r.requested_at_utc, r.resolution_reason, r.resolved_at_utc, r.committed_at_utc,
                   COALESCE(
                       r.total_refund_cents,
                       (SELECT SUM(rl.refund_amount_cents) FROM cafe_sale_refund_lines rl WHERE rl.refund_id = r.refund_id)
                   )
            FROM cafe_sale_refunds r
            LEFT JOIN sales s ON s.id = r.sale_id
            WHERE r.store_id = $store
            ORDER BY r.requested_at_utc DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));

        List<object> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new
            {
                refundId = reader.GetString(0),
                saleId = reader.GetString(1),
                receiptNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                version = reader.GetInt32(3),
                state = reader.GetString(4),
                reason = reader.GetString(5),
                requestedAtUtc = reader.GetString(6),
                resolutionReason = reader.IsDBNull(7) ? null : reader.GetString(7),
                resolvedAtUtc = reader.IsDBNull(8) ? null : reader.GetString(8),
                committedAtUtc = reader.IsDBNull(9) ? null : reader.GetString(9),
                totalRefundCents = reader.IsDBNull(10) ? (long?)null : reader.GetInt64(10),
            });
        }

        return rows;
    }

    private static IReadOnlyList<object> ReadRefundableSales(SqliteConnectionFactory factory, Guid storeId, string? search)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.receipt_no, s.created_at, s.total_cents,
                   COALESCE((SELECT SUM(p.amount_cents) FROM payments p WHERE p.sale_id = s.id AND p.method = $cash), 0),
                   COALESCE((SELECT SUM(p.amount_cents) FROM payments p WHERE p.sale_id = s.id AND p.method = $ar), 0),
                   s.change_cents
            FROM sales s
            WHERE s.status = $completed
              AND EXISTS (SELECT 1 FROM deployment_identity di WHERE di.store_id = $store)
              AND NOT EXISTS (
                  SELECT 1
                  FROM cafe_order_sale_links osl
                  INNER JOIN cafe_orders co ON co.order_id = osl.order_id
                  WHERE osl.sale_id = s.id AND co.store_id != $store
              )
              AND ($search = '' OR LOWER(s.receipt_no) LIKE LOWER($like))
            ORDER BY s.created_at DESC
            LIMIT 40;
            """;
        string cleanSearch = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$cash", Convert.ToInt32(PaymentMethod.Cash));
        command.Parameters.AddWithValue("$ar", Convert.ToInt32(PaymentMethod.AccountsReceivable));
        command.Parameters.AddWithValue("$completed", Convert.ToInt32(SaleStatus.Completed));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$search", cleanSearch);
        command.Parameters.AddWithValue("$like", $"%{cleanSearch}%");

        List<(string SaleId, string ReceiptNumber, string CreatedAt, long TotalCents, long CashNetCents, long ArCents)> sales = [];
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                long cashGross = reader.GetInt64(4);
                long change = reader.GetInt64(6);
                long cashNet = Math.Max(0, cashGross - change);
                sales.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    cashNet,
                    reader.GetInt64(5)));
            }
        }

        List<object> result = [];
        foreach (var sale in sales)
        {
            using SqliteCommand itemsCmd = connection.CreateCommand();
            itemsCmd.CommandText = """
                SELECT si.id, si.product_id, si.product_name_snapshot, si.qty_x1000, si.unit_price_cents, si.line_total_cents,
                       p.track_stock,
                       COALESCE((
                           SELECT SUM(rl.refund_quantity_x1000)
                           FROM cafe_sale_refund_lines rl
                           INNER JOIN cafe_sale_refunds cr ON cr.refund_id = rl.refund_id
                           WHERE rl.sale_item_id = si.id AND cr.state != 'Rejected'
                       ), 0)
                FROM sale_items si
                INNER JOIN products p ON p.id = si.product_id
                WHERE si.sale_id = $sale;
                """;
            itemsCmd.Parameters.AddWithValue("$sale", sale.SaleId);

            List<object> items = [];
            using SqliteDataReader itemReader = itemsCmd.ExecuteReader();
            while (itemReader.Read())
            {
                long origQty = itemReader.GetInt64(3);
                long committedQty = itemReader.GetInt64(7);
                items.Add(new
                {
                    saleItemId = itemReader.GetString(0),
                    productId = itemReader.GetString(1),
                    productName = itemReader.GetString(2),
                    quantityX1000 = origQty,
                    remainingQuantityX1000 = Math.Max(0L, origQty - committedQty),
                    unitPriceCents = itemReader.GetInt64(4),
                    lineTotalCents = itemReader.GetInt64(5),
                    trackStock = itemReader.GetInt32(6) != 0,
                });
            }

            result.Add(new
            {
                saleId = sale.SaleId,
                receiptNumber = sale.ReceiptNumber,
                createdAtUtc = sale.CreatedAt,
                totalCents = sale.TotalCents,
                cashNetCents = sale.CashNetCents,
                arCents = sale.ArCents,
                items,
            });
        }

        return result;
    }

    private static IResult? RequireAny(HttpContext httpContext, LocalApiSessionService sessions, params string[] permissions)
    {
        if (LocalApiPermissionGuards.RequireAuthenticatedSession(httpContext, sessions) is IResult unauth)
            return unauth;

        LocalApiSession? session = sessions.GetRequiredSession(httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName]);
        if (session is null)
            return Results.Json(new { code = "Auth.AuthenticationRequired", error = "Authenticated operator session is required." }, statusCode: StatusCodes.Status401Unauthorized);

        if (permissions.Any(p => LocalApiPermissionGuards.RequirePermission(httpContext, sessions, p) is null))
            return null;

        return Results.Json(new { code = "Auth.Forbidden", error = "Insufficient permissions for Cafe refund operations." }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<Result<LocalRefundAuthority>> ResolveAuthorityAsync(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        SalrynLocalComposition composition,
        CancellationToken cancellationToken)
    {
        LocalApiSession? session = sessions.GetRequiredSession(httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName]);
        if (session is null)
        {
            return Result<LocalRefundAuthority>.Fail(Error.Unauthorized("Auth.AuthenticationRequired", "Authenticated local operator session is required."));
        }

        Result<DeploymentIdentitySnapshot?> deployment = await composition.DeploymentIdentityRepository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (deployment.IsFailure)
            return Result<LocalRefundAuthority>.Fail(deployment.ErrorDetail);

        if (deployment.Value is null || deployment.Value.Store.StoreId == Guid.Empty || deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalRefundAuthority>.Fail(Error.Conflict("CafeRefund.DeploymentIdentityMissing", "Configured StoreId and local TerminalId are required."));
        }

        return Result<LocalRefundAuthority>.Success(new LocalRefundAuthority(
            deployment.Value.Store.StoreId,
            deployment.Value.Terminal.TerminalId,
            session.UserId));
    }

    private static Guid? FindOpenCashierSession(SqliteConnectionFactory factory, Guid actorUserId)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM cashier_sessions WHERE user_id = $actor AND status = 1 ORDER BY opened_at DESC LIMIT 1;";
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        object? scalar = command.ExecuteScalar();
        return scalar is string s && Guid.TryParse(s, out Guid id) ? id : null;
    }

    private static Guid DeriveDeterministicGuid(string canonical)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToError(result.ErrorDetail);

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
        return Results.Json(new { code = error.Code, error = error.Message }, statusCode: statusCode);
    }

    private sealed record LocalRefundAuthority(Guid StoreId, Guid DeviceId, Guid ActorUserId);

    private sealed record RefundLineRequest(
        Guid? RefundLineId,
        Guid SaleItemId,
        long QuantityX1000,
        string StockDisposition,
        string? OperatorNote);

    private sealed record RefundCreateRequest(
        Guid? OperationId,
        Guid? RefundId,
        Guid SaleId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc,
        string Reason,
        IReadOnlyList<RefundLineRequest>? Lines);

    private sealed record RefundResolveRequest(
        Guid? OperationId,
        Guid SaleId,
        int ExpectedVersion,
        bool Approve,
        string? Reason,
        DateTimeOffset? OccurredAtUtc);

    private sealed record RefundSettlementRequest(
        Guid? SettlementPartId,
        string Method,
        long AmountCents);

    private sealed record RefundCommitRequest(
        Guid? OperationId,
        Guid SaleId,
        int ExpectedVersion,
        DateTimeOffset? OccurredAtUtc,
        IReadOnlyList<RefundSettlementRequest>? Settlements);
}
