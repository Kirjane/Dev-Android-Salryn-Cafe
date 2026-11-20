using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// Shared canonical C5 refund participant for central apply and remote projection.
/// It applies already-authorized semantic evidence without creating a local outbox operation.
/// </summary>
public sealed class SharedSqliteCafeRefundParticipant
{
    public Result<bool> ApplyCanonical(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            CafeRefundValidationResult validation =
                CafeRefundOperationV1Validator.ValidateEnvelope(
                    envelope,
                    payload,
                    payload.StoreId);
            if (!validation.IsValid)
            {
                return Result<bool>.Fail(Error.Validation(
                    validation.ErrorCode ?? "CafeRefundSync.Invalid",
                    validation.ErrorMessage ?? "Canonical refund operation is invalid."));
            }

            Result<bool>? replay = TryReplay(context, envelope, payload);
            if (replay is not null)
                return replay;

            EnsureRefundAuthority(context, payload);
            EnsureCompletedSale(context, payload.SaleId);

            return payload.OperationType switch
            {
                CafeRefundSyncContract.Requested =>
                    ApplyRequested(context, envelope, payload),
                CafeRefundSyncContract.Resolved =>
                    ApplyResolved(context, envelope, payload),
                CafeRefundSyncContract.Committed =>
                    ApplyCommitted(context, envelope, payload),
                _ => Result<bool>.Fail(Error.Validation(
                    "CafeRefundSync.Unsupported",
                    "Unsupported canonical refund operation."))
            };
        }
        catch (CafeRefundCanonicalApplyException ex)
        {
            return Result<bool>.Fail(Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or
            OverflowException or FormatException)
        {
            return Result<bool>.Fail(Error.Unexpected(
                "CafeRefundSync.ApplyFailed",
                ex.Message));
        }
    }

    public Result<bool> VerifyCanonical(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            CafeRefundValidationResult validation =
                CafeRefundOperationV1Validator.ValidateEnvelope(
                    envelope,
                    payload,
                    payload.StoreId);
            if (!validation.IsValid)
            {
                return Result<bool>.Fail(Error.Conflict(
                    validation.ErrorCode ?? "CafeRefundSync.Invalid",
                    validation.ErrorMessage ?? "Canonical refund operation is invalid."));
            }

            using SqliteCommand command = context.Connection.CreateCommand();
            command.Transaction = context.Transaction;
            command.CommandText = """
                SELECT device_id,store_id,refund_id,sale_id,operation_type,
                       payload_hash,expected_version,resulting_version,
                       result_state,actor_user_id,occurred_at_utc
                FROM cafe_sale_refund_operations
                WHERE operation_id=$operation
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Result<bool>.Fail(Error.Conflict(
                    "CafeRefundSync.OriginOperationMissing",
                    "Origin echo has no matching durable refund operation evidence."));
            }

            bool matches =
                Guid.Parse(reader.GetString(0)) == envelope.DeviceId &&
                Guid.Parse(reader.GetString(1)) == payload.StoreId &&
                Guid.Parse(reader.GetString(2)) == payload.RefundId &&
                Guid.Parse(reader.GetString(3)) == payload.SaleId &&
                string.Equals(reader.GetString(4), payload.OperationType, StringComparison.Ordinal) &&
                string.Equals(reader.GetString(5), envelope.PayloadHash, StringComparison.Ordinal) &&
                reader.GetInt32(6) == payload.ExpectedVersion &&
                reader.GetInt32(7) == payload.ResultingVersion &&
                string.Equals(reader.GetString(8), payload.ToState, StringComparison.Ordinal) &&
                Guid.Parse(reader.GetString(9)) == payload.ActorUserId &&
                DateTimeOffset.Parse(reader.GetString(10)).ToUniversalTime() ==
                    payload.OccurredAtUtc.ToUniversalTime();

            if (!matches)
            {
                return Result<bool>.Fail(Error.Conflict(
                    "CafeRefundSync.OriginOperationMismatch",
                    "Origin echo conflicts with durable refund operation evidence."));
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<bool>.Fail(Error.Unexpected(
                "CafeRefundSync.VerifyFailed",
                ex.Message));
        }
    }

    private static Result<bool>? TryReplay(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT device_id,store_id,refund_id,sale_id,operation_type,
                   payload_hash,expected_version,resulting_version,
                   result_state,actor_user_id
            FROM cafe_sale_refund_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        bool exact =
            Guid.Parse(reader.GetString(0)) == envelope.DeviceId &&
            Guid.Parse(reader.GetString(1)) == payload.StoreId &&
            Guid.Parse(reader.GetString(2)) == payload.RefundId &&
            Guid.Parse(reader.GetString(3)) == payload.SaleId &&
            string.Equals(reader.GetString(4), payload.OperationType, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(5), envelope.PayloadHash, StringComparison.Ordinal) &&
            reader.GetInt32(6) == payload.ExpectedVersion &&
            reader.GetInt32(7) == payload.ResultingVersion &&
            string.Equals(reader.GetString(8), payload.ToState, StringComparison.Ordinal) &&
            Guid.Parse(reader.GetString(9)) == payload.ActorUserId;

        return exact
            ? Result<bool>.Success(false)
            : Result<bool>.Fail(Error.Conflict(
                "CafeRefundSync.OperationCollision",
                "Refund operation ID already exists with different immutable evidence."));
    }

    private static Result<bool> ApplyRequested(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        if (RefundExists(context, payload.RefundId))
        {
            return Result<bool>.Fail(Error.Conflict(
                "CafeRefundSync.RefundIdConflict",
                "Refund identity already exists without matching operation evidence."));
        }

        foreach (CafeRefundLineV1 line in payload.Lines)
            ValidateRequestedLine(context, payload, line);

        using (SqliteCommand insert = context.Connection.CreateCommand())
        {
            insert.Transaction = context.Transaction;
            insert.CommandText = """
                INSERT INTO cafe_sale_refunds(
                    refund_id,sale_id,store_id,current_version,state,reason,
                    requested_by_user_id,requested_at_utc,request_hash,
                    resolved_by_user_id,resolved_at_utc,resolution_reason,
                    committed_by_user_id,committed_at_utc,total_refund_cents)
                VALUES(
                    $refund,$sale,$store,$version,'Requested',$reason,
                    $actor,$at,$requestHash,NULL,NULL,NULL,NULL,NULL,NULL);
                """;
            insert.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
            insert.Parameters.AddWithValue("$sale", payload.SaleId.ToString("D"));
            insert.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
            insert.Parameters.AddWithValue("$version", payload.ResultingVersion);
            insert.Parameters.AddWithValue("$reason", payload.RequestReason);
            insert.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$requestHash", payload.RequestHash);
            insert.ExecuteNonQuery();
        }

        foreach (CafeRefundLineV1 line in payload.Lines)
            InsertLine(context, payload.RefundId, line);

        InsertOperationAndEvent(context, envelope, payload);
        return Result<bool>.Success(true);
    }

    private static Result<bool> ApplyResolved(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        RefundState current = ReadRefund(context, payload.RefundId)
            ?? throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.RefundMissing",
                "Refund request does not exist.");

        ValidateRefundIdentityAndVersion(current, payload);

        if (!string.Equals(current.State, CafeRefundSyncContract.StateRequested, StringComparison.Ordinal) ||
            !string.Equals(current.RequestHash, payload.RequestHash, StringComparison.Ordinal) ||
            !string.Equals(current.Reason, payload.RequestReason, StringComparison.Ordinal))
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ResolveAuthorityConflict",
                "Refund resolution conflicts with retained request evidence.");
        }

        ValidateRetainedLines(context, payload);

        using SqliteCommand update = context.Connection.CreateCommand();
        update.Transaction = context.Transaction;
        update.CommandText = """
            UPDATE cafe_sale_refunds
            SET current_version=$version,
                state=$state,
                resolved_by_user_id=$actor,
                resolved_at_utc=$at,
                resolution_reason=$reason
            WHERE refund_id=$refund
              AND current_version=$expected
              AND state='Requested';
            """;
        update.Parameters.AddWithValue("$version", payload.ResultingVersion);
        update.Parameters.AddWithValue("$state", payload.ToState);
        update.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        update.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        update.Parameters.AddWithValue("$reason",
            payload.ResolutionReason is null ? DBNull.Value : payload.ResolutionReason);
        update.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
        update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);

        if (update.ExecuteNonQuery() != 1)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ResolveConcurrentConflict",
                "Refund resolution lost version ownership.");
        }

        InsertOperationAndEvent(context, envelope, payload);
        return Result<bool>.Success(true);
    }

    private static Result<bool> ApplyCommitted(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        RefundState current = ReadRefund(context, payload.RefundId)
            ?? throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.RefundMissing",
                "Approved refund does not exist.");

        ValidateRefundIdentityAndVersion(current, payload);

        if (!string.Equals(current.State, CafeRefundSyncContract.StateApproved, StringComparison.Ordinal) ||
            !string.Equals(current.RequestHash, payload.RequestHash, StringComparison.Ordinal) ||
            !string.Equals(current.Reason, payload.RequestReason, StringComparison.Ordinal))
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.CommitAuthorityConflict",
                "Refund commit conflicts with retained approved evidence.");
        }

        ValidateRetainedLines(context, payload);
        RevalidateRemainingQuantities(context, payload);
        ValidateSettlementAuthority(context, payload);

        foreach (CafeRefundLineV1 line in payload.Lines)
        {
            if (string.Equals(line.StockDisposition, CafeRefundSyncContract.Restock, StringComparison.Ordinal))
                ApplyReturnRestock(context, payload, line);
        }

        foreach (CafeRefundSettlementV1 settlement in payload.Settlements)
        {
            if (string.Equals(settlement.Method, CafeRefundSyncContract.Cash, StringComparison.Ordinal))
                ApplyCashRefund(context, payload, settlement);
            else
                ApplyArCredit(context, payload, settlement);

            InsertSettlement(context, payload.RefundId, settlement);
        }

        using SqliteCommand update = context.Connection.CreateCommand();
        update.Transaction = context.Transaction;
        update.CommandText = """
            UPDATE cafe_sale_refunds
            SET current_version=$version,
                state='Committed',
                committed_by_user_id=$actor,
                committed_at_utc=$at,
                total_refund_cents=$total
            WHERE refund_id=$refund
              AND current_version=$expected
              AND state='Approved';
            """;
        update.Parameters.AddWithValue("$version", payload.ResultingVersion);
        update.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        update.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        update.Parameters.AddWithValue("$total", payload.RefundTotalCents);
        update.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
        update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);

        if (update.ExecuteNonQuery() != 1)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.CommitConcurrentConflict",
                "Refund commit lost version ownership.");
        }

        InsertOperationAndEvent(context, envelope, payload);
        return Result<bool>.Success(true);
    }

    private static void ValidateRequestedLine(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload,
        CafeRefundLineV1 line)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT i.product_id,i.product_name_snapshot,i.qty_x1000,
                   i.unit_price_cents,i.discount_cents,i.line_total_cents,
                   p.track_stock
            FROM sale_items i
            INNER JOIN products p ON p.id=i.product_id
            WHERE i.id=$item AND i.sale_id=$sale
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
        command.Parameters.AddWithValue("$sale", payload.SaleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.SaleItemMissing",
                "Refund line is not part of the source sale.");
        }

        Guid productId = Guid.Parse(reader.GetString(0));
        string name = reader.GetString(1);
        long originalQty = reader.GetInt64(2);
        long unit = reader.GetInt64(3);
        long discount = reader.GetInt64(4);
        long lineTotal = reader.GetInt64(5);
        bool trackStock = reader.GetInt32(6) != 0;
        reader.Close();

        if (productId != line.ProductId ||
            !string.Equals(name, line.ProductNameSnapshot, StringComparison.Ordinal) ||
            originalQty != line.OriginalQuantityX1000 ||
            unit != line.FrozenUnitPriceCents ||
            discount != line.FrozenDiscountCents)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.FrozenEvidenceMismatch",
                "Refund line does not match frozen source-sale evidence.");
        }

        (long priorQty, long priorAmount) = ReadPriorCommittedReturn(context, line.SaleItemId);
        long remaining = checked(originalQty - priorQty);
        if (line.RefundQuantityX1000 > remaining)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.OverRefundQuantity",
                "Refund quantity exceeds remaining source-sale quantity.");
        }

        long cumulativeQty = checked(priorQty + line.RefundQuantityX1000);
        long cumulativeAmount =
            cumulativeQty == originalQty
                ? lineTotal
                : checked((long)decimal.Round(
                    (decimal)lineTotal * cumulativeQty / originalQty,
                    0,
                    MidpointRounding.AwayFromZero));
        long expectedAmount = checked(cumulativeAmount - priorAmount);

        if (expectedAmount != line.RefundAmountCents)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.RefundAmountMismatch",
                "Refund line amount is not the deterministic frozen-evidence amount.");
        }

        if (string.Equals(line.StockDisposition, CafeRefundSyncContract.Restock, StringComparison.Ordinal) &&
            !trackStock)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.RestockNotStockTracked",
                "Restock disposition requires a stock-tracked product.");
        }
    }

    private static void ValidateRetainedLines(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT refund_line_id,sale_item_id,product_id,product_name_snapshot,
                   original_quantity_x1000,refund_quantity_x1000,
                   frozen_unit_price_cents,frozen_discount_cents,
                   refund_amount_cents,stock_disposition,operator_note
            FROM cafe_sale_refund_lines
            WHERE refund_id=$refund
            ORDER BY refund_line_id;
            """;
        command.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));

        List<CafeRefundLineV1> retained = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            retained.Add(new CafeRefundLineV1(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        CafeRefundLineV1[] canonical = payload.Lines.OrderBy(x => x.RefundLineId).ToArray();
        if (retained.Count != canonical.Length)
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.LineSetMismatch",
                "Refund line set differs from retained request evidence.");

        for (int i = 0; i < retained.Count; i++)
        {
            if (retained[i] != canonical[i])
            {
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.LineEvidenceMismatch",
                    "Refund line evidence differs from retained request evidence.");
            }
        }
    }

    private static void ValidateSettlementAuthority(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload)
    {
        using SqliteCommand sale = context.Connection.CreateCommand();
        sale.Transaction = context.Transaction;
        sale.CommandText = """
            SELECT s.customer_id,s.change_cents,
                   COALESCE(SUM(CASE WHEN p.method=$cash THEN p.amount_cents ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN p.method=$ar THEN p.amount_cents ELSE 0 END),0)
            FROM sales s
            LEFT JOIN payments p ON p.sale_id=s.id
            WHERE s.id=$sale
            GROUP BY s.id,s.customer_id,s.change_cents;
            """;
        sale.Parameters.AddWithValue("$sale", payload.SaleId.ToString("D"));
        sale.Parameters.AddWithValue("$cash", Convert.ToInt32(PaymentMethod.Cash));
        sale.Parameters.AddWithValue("$ar", Convert.ToInt32(PaymentMethod.AccountsReceivable));

        using SqliteDataReader reader = sale.ExecuteReader();
        if (!reader.Read())
            throw new CafeRefundCanonicalApplyException("CafeRefundSync.SaleMissing", "Source sale is missing.");

        Guid? customerId = reader.IsDBNull(0) ? null : Guid.Parse(reader.GetString(0));
        long change = reader.GetInt64(1);
        long cashTender = reader.GetInt64(2);
        long arTender = reader.GetInt64(3);
        reader.Close();

        long priorCash = ReadPriorSettlementTotal(context, payload.SaleId, CafeRefundSyncContract.Cash);
        long priorAr = ReadPriorSettlementTotal(context, payload.SaleId, CafeRefundSyncContract.AccountsReceivable);

        CafeRefundSettlementV1? cash = payload.Settlements.SingleOrDefault(
            x => string.Equals(x.Method, CafeRefundSyncContract.Cash, StringComparison.Ordinal));
        CafeRefundSettlementV1? ar = payload.Settlements.SingleOrDefault(
            x => string.Equals(x.Method, CafeRefundSyncContract.AccountsReceivable, StringComparison.Ordinal));

        if (cash is not null &&
            cash.AmountCents > checked(cashTender - change - priorCash))
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.CashAuthorityExceeded",
                "Cash refund exceeds remaining retained cash authority.");
        }

        if (ar is not null)
        {
            if (ar.AmountCents > checked(arTender - priorAr))
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.ArAuthorityExceeded",
                    "AR refund exceeds remaining original AR authority.");

            if (!customerId.HasValue)
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.ArCustomerMissing",
                    "AR refund requires the original sale customer.");

            EnsureDeterministicArAllocation(
                context,
                customerId.Value,
                payload.SaleId,
                ar.AmountCents);
        }

        if (cash is not null)
        {
            using SqliteCommand session = context.Connection.CreateCommand();
            session.Transaction = context.Transaction;
            session.CommandText = """
                SELECT COUNT(*)
                FROM cashier_sessions
                WHERE id=$session AND user_id=$actor AND status=$open;
                """;
            session.Parameters.AddWithValue("$session", payload.CashierSessionId!.Value.ToString("D"));
            session.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            session.Parameters.AddWithValue("$open", Convert.ToInt32(CashierSessionStatus.Open));

            if (Convert.ToInt64(session.ExecuteScalar() ?? 0L) != 1)
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.OpenCashierSessionRequired",
                    "Cash refund requires the actor's exact open cashier session.");
        }
    }

    private static void ApplyCashRefund(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload,
        CafeRefundSettlementV1 settlement)
    {
        Result result = SqliteCashMovementPersistence.Insert(
            context.Connection,
            context.Transaction,
            new CashMovement
            {
                Id = settlement.CashMovementId!.Value,
                CashierSessionId = payload.CashierSessionId!.Value,
                ActorUserId = payload.ActorUserId,
                MovementType = CashMovementType.CashRefund,
                Delta = Money.FromCents(-settlement.AmountCents),
                ReferenceType = "REFUND",
                ReferenceId = payload.RefundId,
                ReversalOfMovementId = null,
                Reason = "Cafe sale refund",
                CreatedAt = settlement.CreatedAtUtc.ToUniversalTime()
            });

        if (result.IsFailure)
            throw new CafeRefundCanonicalApplyException(
                result.ErrorDetail.Code,
                result.ErrorDetail.Message);
    }

    private static void ApplyArCredit(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload,
        CafeRefundSettlementV1 settlement)
    {
        Guid customerId = ReadSaleCustomer(context, payload.SaleId)
            ?? throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ArCustomerMissing",
                "AR refund requires the original sale customer.");

        long balance = ReadCustomerBalance(context, customerId);
        long next = checked(balance - settlement.AmountCents);
        if (next < 0)
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ArBalanceConflict",
                "AR refund credit would drive the customer balance below zero.");

        using SqliteCommand insert = context.Connection.CreateCommand();
        insert.Transaction = context.Transaction;
        insert.CommandText = """
            INSERT INTO accounts_receivable_entries(
                id,customer_id,sale_id,collection_id,entry_type,
                debit_cents,credit_cents,balance_after_cents,
                due_date,note,created_by_user_id,created_at,refund_id)
            VALUES(
                $id,$customer,$sale,NULL,'AdjustmentCredit',
                0,$credit,$balance,NULL,$note,$actor,$at,$refund);
            """;
        insert.Parameters.AddWithValue("$id", settlement.ReceivableEntryId!.Value.ToString("D"));
        insert.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        insert.Parameters.AddWithValue("$sale", payload.SaleId.ToString("D"));
        insert.Parameters.AddWithValue("$credit", settlement.AmountCents);
        insert.Parameters.AddWithValue("$balance", next);
        insert.Parameters.AddWithValue("$note", "Cafe sale refund AR credit");
        insert.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        insert.Parameters.AddWithValue("$at", settlement.CreatedAtUtc.ToUniversalTime().ToString("O"));
        insert.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
        insert.ExecuteNonQuery();
    }

    private static void ApplyReturnRestock(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload,
        CafeRefundLineV1 line)
    {
        long current;
        using (SqliteCommand read = context.Connection.CreateCommand())
        {
            read.Transaction = context.Transaction;
            read.CommandText = """
                SELECT stock_on_hand_x1000,track_stock,is_active
                FROM products
                WHERE id=$product
                LIMIT 1;
                """;
            read.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));

            using SqliteDataReader reader = read.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(1) != 1 || reader.GetInt32(2) != 1)
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.RestockProductUnavailable",
                    "Restock disposition requires an active stock-tracked product.");

            current = reader.GetInt64(0);
        }

        long next = checked(current + line.RefundQuantityX1000);
        using (SqliteCommand update = context.Connection.CreateCommand())
        {
            update.Transaction = context.Transaction;
            update.CommandText = """
                UPDATE products
                SET stock_on_hand_x1000=$stock,updated_at=$at
                WHERE id=$product AND track_stock=1 AND is_active=1;
                """;
            update.Parameters.AddWithValue("$stock", next);
            update.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.RestockConcurrentConflict",
                    "Product stock was unavailable for return restock.");
        }

        Guid movementId = DeterministicGuid(
            "salryn-c5-refund-restock-v1",
            line.RefundLineId.ToString("D"));

        using SqliteCommand movement = context.Connection.CreateCommand();
        movement.Transaction = context.Transaction;
        movement.CommandText = """
            INSERT INTO stock_movements(
                id,product_id,movement_type,qty_change_x1000,
                cost_price_cents,reference_type,reference_id,
                reason,user_id,created_at)
            VALUES(
                $id,$product,$type,$qty,NULL,'REFUND',$refund,
                $reason,$actor,$at);
            """;
        movement.Parameters.AddWithValue("$id", movementId.ToString("D"));
        movement.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
        movement.Parameters.AddWithValue("$type", Convert.ToInt32(StockMovementType.ReturnRestock));
        movement.Parameters.AddWithValue("$qty", line.RefundQuantityX1000);
        movement.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
        movement.Parameters.AddWithValue("$reason", "Cafe refund return restock");
        movement.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        movement.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        movement.ExecuteNonQuery();
    }

    private static void InsertLine(
        SqliteSharedTransactionContext context,
        Guid refundId,
        CafeRefundLineV1 line)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            INSERT INTO cafe_sale_refund_lines(
                refund_line_id,refund_id,sale_item_id,product_id,
                product_name_snapshot,original_quantity_x1000,
                refund_quantity_x1000,frozen_unit_price_cents,
                frozen_discount_cents,refund_amount_cents,
                stock_disposition,operator_note)
            VALUES(
                $line,$refund,$item,$product,$name,$originalQty,$refundQty,
                $unit,$discount,$amount,$disposition,$note);
            """;
        command.Parameters.AddWithValue("$line", line.RefundLineId.ToString("D"));
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        command.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
        command.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
        command.Parameters.AddWithValue("$name", line.ProductNameSnapshot);
        command.Parameters.AddWithValue("$originalQty", line.OriginalQuantityX1000);
        command.Parameters.AddWithValue("$refundQty", line.RefundQuantityX1000);
        command.Parameters.AddWithValue("$unit", line.FrozenUnitPriceCents);
        command.Parameters.AddWithValue("$discount", line.FrozenDiscountCents);
        command.Parameters.AddWithValue("$amount", line.RefundAmountCents);
        command.Parameters.AddWithValue("$disposition", line.StockDisposition);
        command.Parameters.AddWithValue("$note", line.OperatorNote is null ? DBNull.Value : line.OperatorNote);
        command.ExecuteNonQuery();
    }

    private static void InsertSettlement(
        SqliteSharedTransactionContext context,
        Guid refundId,
        CafeRefundSettlementV1 settlement)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            INSERT INTO cafe_sale_refund_settlements(
                settlement_part_id,refund_id,method,amount_cents,
                cash_movement_id,receivable_entry_id,created_at_utc)
            VALUES($id,$refund,$method,$amount,$cash,$ar,$at);
            """;
        command.Parameters.AddWithValue("$id", settlement.SettlementPartId.ToString("D"));
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        command.Parameters.AddWithValue("$method", settlement.Method);
        command.Parameters.AddWithValue("$amount", settlement.AmountCents);
        command.Parameters.AddWithValue("$cash",
            settlement.CashMovementId.HasValue ? settlement.CashMovementId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$ar",
            settlement.ReceivableEntryId.HasValue ? settlement.ReceivableEntryId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$at", settlement.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertOperationAndEvent(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload)
    {
        using (SqliteCommand operation = context.Connection.CreateCommand())
        {
            operation.Transaction = context.Transaction;
            operation.CommandText = """
                INSERT INTO cafe_sale_refund_operations(
                    operation_id,device_id,store_id,refund_id,sale_id,
                    operation_type,payload_hash,expected_version,
                    resulting_version,result_state,actor_user_id,occurred_at_utc)
                VALUES(
                    $operation,$device,$store,$refund,$sale,
                    $type,$hash,$expected,$resulting,$state,$actor,$at);
                """;
            operation.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
            operation.Parameters.AddWithValue("$device", envelope.DeviceId.ToString("D"));
            operation.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
            operation.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
            operation.Parameters.AddWithValue("$sale", payload.SaleId.ToString("D"));
            operation.Parameters.AddWithValue("$type", payload.OperationType);
            operation.Parameters.AddWithValue("$hash", envelope.PayloadHash);
            operation.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
            operation.Parameters.AddWithValue("$resulting", payload.ResultingVersion);
            operation.Parameters.AddWithValue("$state", payload.ToState);
            operation.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            operation.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            operation.ExecuteNonQuery();
        }

        string action = payload.ToState;
        using SqliteCommand evt = context.Connection.CreateCommand();
        evt.Transaction = context.Transaction;
        evt.CommandText = """
            INSERT INTO cafe_sale_refund_events(
                event_id,operation_id,refund_id,sale_id,
                expected_version,resulting_version,action,
                from_state,to_state,actor_user_id,occurred_at_utc,
                reason,content_hash)
            VALUES(
                $event,$operation,$refund,$sale,
                $expected,$resulting,$action,
                $from,$to,$actor,$at,$reason,$hash);
            """;
        evt.Parameters.AddWithValue("$event", envelope.OperationId.ToString("D"));
        evt.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        evt.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
        evt.Parameters.AddWithValue("$sale", payload.SaleId.ToString("D"));
        evt.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
        evt.Parameters.AddWithValue("$resulting", payload.ResultingVersion);
        evt.Parameters.AddWithValue("$action", action);
        evt.Parameters.AddWithValue("$from", payload.FromState is null ? DBNull.Value : payload.FromState);
        evt.Parameters.AddWithValue("$to", payload.ToState);
        evt.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        evt.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        evt.Parameters.AddWithValue("$reason",
            payload.ResolutionReason ?? payload.RequestReason);
        evt.Parameters.AddWithValue("$hash", envelope.PayloadHash);
        evt.ExecuteNonQuery();
    }

    private static void ValidateRefundIdentityAndVersion(
        RefundState current,
        CafeRefundOperationV1 payload)
    {
        if (current.SaleId != payload.SaleId ||
            current.StoreId != payload.StoreId ||
            current.Version != payload.ExpectedVersion)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.VersionOrAuthorityConflict",
                "Refund sale/store/version authority does not match canonical operation.");
        }
    }

    private static void EnsureRefundAuthority(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload)
    {
        string permission =
            string.Equals(
                payload.OperationType,
                CafeRefundSyncContract.Requested,
                StringComparison.Ordinal)
                ? SalrynPermissions.SalesRefundRequest
                : SalrynPermissions.SalesRefundApprove;

        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$actor
              AND u.status=1
              AND rp.permission=$permission;
            """;
        command.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$permission", permission);

        if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.PermissionRequired",
                "Refund semantic operation lacks required actor permission: " + permission);
        }
    }

    private static void EnsureCompletedSale(
        SqliteSharedTransactionContext context,
        Guid saleId)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = "SELECT status FROM sales WHERE id=$sale LIMIT 1;";
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        object? value = command.ExecuteScalar();
        if (value is null or DBNull ||
            (SaleStatus)Convert.ToInt32(value) != SaleStatus.Completed)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.SourceSaleNotRefundable",
                "Only an existing completed non-voided sale may be refunded.");
        }
    }

    private static bool RefundExists(
        SqliteSharedTransactionContext context,
        Guid refundId)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = "SELECT COUNT(*) FROM cafe_sale_refunds WHERE refund_id=$refund;";
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static RefundState? ReadRefund(
        SqliteSharedTransactionContext context,
        Guid refundId)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT sale_id,store_id,current_version,state,reason,request_hash
            FROM cafe_sale_refunds
            WHERE refund_id=$refund
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new RefundState(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5))
            : null;
    }

    private static void RevalidateRemainingQuantities(
        SqliteSharedTransactionContext context,
        CafeRefundOperationV1 payload)
    {
        foreach (CafeRefundLineV1 line in payload.Lines)
        {
            using SqliteCommand command = context.Connection.CreateCommand();
            command.Transaction = context.Transaction;
            command.CommandText = """
                SELECT COALESCE(SUM(l.refund_quantity_x1000),0)
                FROM cafe_sale_refund_lines l
                INNER JOIN cafe_sale_refunds r ON r.refund_id=l.refund_id
                WHERE l.sale_item_id=$item
                  AND r.state='Committed'
                  AND r.refund_id<>$refund;
                """;
            command.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
            command.Parameters.AddWithValue("$refund", payload.RefundId.ToString("D"));
            long prior = Convert.ToInt64(command.ExecuteScalar() ?? 0L);
            if (checked(prior + line.RefundQuantityX1000) > line.OriginalQuantityX1000)
                throw new CafeRefundCanonicalApplyException(
                    "CafeRefundSync.ConcurrentOverRefund",
                    "Another committed refund consumed the remaining refundable quantity.");
        }
    }

    private static (long Quantity, long Amount) ReadPriorCommittedReturn(
        SqliteSharedTransactionContext context,
        Guid saleItemId)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(l.refund_quantity_x1000),0),
                   COALESCE(SUM(l.refund_amount_cents),0)
            FROM cafe_sale_refund_lines l
            INNER JOIN cafe_sale_refunds r ON r.refund_id=l.refund_id
            WHERE l.sale_item_id=$item AND r.state='Committed';
            """;
        command.Parameters.AddWithValue("$item", saleItemId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static long ReadPriorSettlementTotal(
        SqliteSharedTransactionContext context,
        Guid saleId,
        string method)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(s.amount_cents),0)
            FROM cafe_sale_refund_settlements s
            INNER JOIN cafe_sale_refunds r ON r.refund_id=s.refund_id
            WHERE r.sale_id=$sale AND r.state='Committed' AND s.method=$method;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        command.Parameters.AddWithValue("$method", method);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static Guid? ReadSaleCustomer(
        SqliteSharedTransactionContext context,
        Guid saleId)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = "SELECT customer_id FROM sales WHERE id=$sale LIMIT 1;";
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is string text ? Guid.Parse(text) : null;
    }

    private static void EnsureDeterministicArAllocation(
        SqliteSharedTransactionContext context,
        Guid customerId,
        Guid saleId,
        long requestedCredit)
    {
        using SqliteCommand source = context.Connection.CreateCommand();
        source.Transaction = context.Transaction;
        source.CommandText = """
            SELECT created_at
            FROM accounts_receivable_entries
            WHERE customer_id=$customer
              AND sale_id=$sale
              AND entry_type='CreditSale'
            LIMIT 1;
            """;
        source.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        source.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        object? sourceAtValue = source.ExecuteScalar();

        if (sourceAtValue is not string sourceAt)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ArDebitEvidenceMissing",
                "Original AR debit evidence is missing for the sale.");
        }

        using SqliteCommand laterCredits = context.Connection.CreateCommand();
        laterCredits.Transaction = context.Transaction;
        laterCredits.CommandText = """
            SELECT COUNT(*)
            FROM accounts_receivable_entries
            WHERE customer_id=$customer
              AND created_at>$sourceAt
              AND entry_type IN ('Collection','AdjustmentCredit')
              AND refund_id IS NULL;
            """;
        laterCredits.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        laterCredits.Parameters.AddWithValue("$sourceAt", sourceAt);

        if (Convert.ToInt64(laterCredits.ExecuteScalar() ?? 0L) != 0)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ArAllocationNeedsReview",
                "Later AR credits/collections prevent deterministic allocation back to this sale.");
        }

        long balance = ReadCustomerBalance(context, customerId);
        if (balance < requestedCredit)
        {
            throw new CafeRefundCanonicalApplyException(
                "CafeRefundSync.ArBalanceConflict",
                "Customer receivable balance is lower than the proposed refund credit.");
        }
    }

    private static long ReadCustomerBalance(
        SqliteSharedTransactionContext context,
        Guid customerId)
    {
        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT balance_after_cents
            FROM accounts_receivable_entries
            WHERE customer_id=$customer
            ORDER BY created_at DESC,id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 0L : Convert.ToInt64(value);
    }

    private static Guid DeterministicGuid(string domain, string value)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(domain + "|" + value));
        Span<byte> guidBytes = bytes.AsSpan(0, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private sealed record RefundState(
        Guid SaleId,
        Guid StoreId,
        int Version,
        string State,
        string Reason,
        string RequestHash);

    private sealed class CafeRefundCanonicalApplyException : Exception
    {
        public CafeRefundCanonicalApplyException(string code, string message)
            : base(message) => Code = code;

        public string Code { get; }
    }
}
