using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SharedSqliteCafeCashParticipant
{
    public Result<bool> ApplyCanonical(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeCashMovementPostedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        Result validation = ValidateForApply(
            context.Connection,
            context.Transaction,
            envelope,
            payload);
        if (validation.IsFailure)
            return Result<bool>.Fail(validation.ErrorDetail);

        Result<bool>? replay = TryReplay(
            context.Connection,
            context.Transaction,
            envelope,
            payload);
        if (replay is not null)
            return replay;

        CashMovementType movementType =
            ToMovementType(payload.MovementType);

        if (MovementExists(
                context.Connection,
                context.Transaction,
                payload.MovementId))
        {
            return Result<bool>.Fail(
                Error.Conflict(
                    "CafeCashSync.MovementConflict",
                    "Cash movement ID already exists without matching C6 operation evidence."));
        }

        if (movementType == CashMovementType.Reversal)
        {
            Result reversal = ValidateReversalSource(
                context.Connection,
                context.Transaction,
                payload);
            if (reversal.IsFailure)
                return Result<bool>.Fail(reversal.ErrorDetail);
        }
        else if (movementType == CashMovementType.InventoryPurchase)
        {
            Result purchase = ValidateInventoryPurchaseReference(
                context.Connection,
                context.Transaction,
                payload.StoreId,
                payload.ReferenceId);
            if (purchase.IsFailure)
                return Result<bool>.Fail(purchase.ErrorDetail);
        }

        CashMovement movement = new()
        {
            Id = payload.MovementId,
            CashierSessionId = payload.CashierSessionId,
            ActorUserId = payload.ActorUserId,
            MovementType = movementType,
            Delta = Money.FromCents(payload.DeltaCents),
            ReferenceType = payload.ReferenceType,
            ReferenceId = payload.ReferenceId,
            ReversalOfMovementId = payload.ReversalOfMovementId,
            Reason = payload.Reason,
            CreatedAt = payload.OccurredAtUtc.ToUniversalTime()
        };

        Result ledger = SqliteCashMovementPersistence.Insert(
            context.Connection,
            context.Transaction,
            movement);
        if (ledger.IsFailure)
            return Result<bool>.Fail(ledger.ErrorDetail);

        InsertOperation(
            context.Connection,
            context.Transaction,
            envelope,
            payload,
            movementType);

        return Result<bool>.Success(true);
    }

    public Result<bool> VerifyCanonical(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        CafeCashMovementPostedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        Result validation = ValidateEnvelope(envelope, payload);
        if (validation.IsFailure)
            return Result<bool>.Fail(validation.ErrorDetail);

        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            SELECT o.device_id,o.store_id,o.actor_user_id,o.cashier_session_id,
                   o.movement_type,o.requested_amount_cents,o.delta_cents,
                   o.reference_type,o.reference_id,o.reversal_of_movement_id,
                   o.reason,o.payload_hash,o.occurred_at_utc,
                   m.movement_type,m.delta_cents,m.reference_type,m.reference_id,
                   m.reversal_of_movement_id,m.actor_user_id,m.cashier_session_id,
                   m.reason,m.created_at_utc
            FROM cafe_cash_operations o
            INNER JOIN cash_movements m
              ON m.id=o.movement_id
            WHERE o.operation_id=$operation
              AND o.movement_id=$movement
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            envelope.OperationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$movement",
            payload.MovementId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Result<bool>.Fail(
                Error.Conflict(
                    "CafeCashSync.OriginFactsMissing",
                    "Canonical C6 cash operation has no matching durable local facts."));
        }

        int expectedType = Convert.ToInt32(
            ToMovementType(payload.MovementType));
        string? reversal = payload.ReversalOfMovementId?.ToString("D");
        string occurred =
            payload.OccurredAtUtc.ToUniversalTime().ToString("O");

        bool matches =
            Guid.Parse(reader.GetString(0)) == envelope.DeviceId &&
            Guid.Parse(reader.GetString(1)) == payload.StoreId &&
            Guid.Parse(reader.GetString(2)) == payload.ActorUserId &&
            Guid.Parse(reader.GetString(3)) == payload.CashierSessionId &&
            reader.GetInt32(4) == expectedType &&
            reader.GetInt64(5) == payload.RequestedAmountCents &&
            reader.GetInt64(6) == payload.DeltaCents &&
            string.Equals(reader.GetString(7), payload.ReferenceType, StringComparison.Ordinal) &&
            Guid.Parse(reader.GetString(8)) == payload.ReferenceId &&
            NullableGuidMatches(reader, 9, payload.ReversalOfMovementId) &&
            string.Equals(reader.GetString(10), payload.Reason, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(11), envelope.PayloadHash, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(12), occurred, StringComparison.Ordinal) &&
            reader.GetInt32(13) == expectedType &&
            reader.GetInt64(14) == payload.DeltaCents &&
            string.Equals(reader.GetString(15), payload.ReferenceType, StringComparison.Ordinal) &&
            Guid.Parse(reader.GetString(16)) == payload.ReferenceId &&
            NullableGuidMatches(reader, 17, payload.ReversalOfMovementId) &&
            Guid.Parse(reader.GetString(18)) == payload.ActorUserId &&
            Guid.Parse(reader.GetString(19)) == payload.CashierSessionId &&
            string.Equals(reader.GetString(20), payload.Reason, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(21), occurred, StringComparison.Ordinal);

        return matches
            ? Result<bool>.Success(false)
            : Result<bool>.Fail(
                Error.Conflict(
                    "CafeCashSync.OriginFactsMismatch",
                    "Canonical C6 cash operation conflicts with retained local movement/operation facts."));
    }

    private static Result ValidateForApply(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeCashMovementPostedV1 payload)
    {
        Result envelopeValidation = ValidateEnvelope(envelope, payload);
        if (envelopeValidation.IsFailure)
            return envelopeValidation;

        using (SqliteCommand store = connection.CreateCommand())
        {
            store.Transaction = transaction;
            store.CommandText = """
                SELECT COUNT(*)
                FROM deployment_identity
                WHERE store_id=$store;
                """;
            store.Parameters.AddWithValue(
                "$store",
                payload.StoreId.ToString("D"));

            if (Convert.ToInt64(store.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeCashSync.StoreAuthorityMismatch",
                        "C6 cash StoreId does not match deployment authority."));
            }
        }

        using (SqliteCommand session = connection.CreateCommand())
        {
            session.Transaction = transaction;
            session.CommandText = """
                SELECT COUNT(*)
                FROM cashier_sessions
                WHERE id=$session
                  AND user_id=$actor
                  AND status=$open;
                """;
            session.Parameters.AddWithValue(
                "$session",
                payload.CashierSessionId.ToString("D"));
            session.Parameters.AddWithValue(
                "$actor",
                payload.ActorUserId.ToString("D"));
            session.Parameters.AddWithValue(
                "$open",
                Convert.ToInt32(CashierSessionStatus.Open));

            if (Convert.ToInt64(session.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeCashSync.CashierSessionConflict",
                        "C6 cash operation requires the exact open cashier session owned by the actor."));
            }
        }

        string permission = RequiredPermission(payload.MovementType);
        using SqliteCommand role = connection.CreateCommand();
        role.Transaction = transaction;
        role.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$actor
              AND u.status=1
              AND rp.permission=$permission;
            """;
        role.Parameters.AddWithValue(
            "$actor",
            payload.ActorUserId.ToString("D"));
        role.Parameters.AddWithValue(
            "$permission",
            permission);

        if (Convert.ToInt64(role.ExecuteScalar() ?? 0L) != 1)
        {
            return Result.Fail(
                Error.Forbidden(
                    "CafeCashSync.PermissionRequired",
                    "C6 cash operation requires permission: " + permission));
        }

        return Result.Success();
    }

    private static Result ValidateEnvelope(
        SyncOperationEnvelopeV1 envelope,
        CafeCashMovementPostedV1 payload)
    {
        if (!string.Equals(
                envelope.OperationType,
                CafeCashMovementSyncContract.OperationType,
                StringComparison.Ordinal) ||
            envelope.SchemaVersion != CafeCashMovementSyncContract.SchemaVersion)
        {
            return Result.Fail(
                Error.Validation(
                    "CafeCashSync.ContractUnsupported",
                    "Envelope is not CashMovementPostedV1 schema version 1."));
        }

        CafeCashMovementValidationResult payloadValidation =
            CafeCashMovementPostedV1Validator.Validate(payload);
        if (!payloadValidation.IsValid)
        {
            return Result.Fail(
                Error.Validation(
                    payloadValidation.ErrorCode ??
                        "CafeCashSync.PayloadInvalid",
                    payloadValidation.ErrorMessage ??
                        "C6 cash payload is invalid."));
        }

        if (envelope.AggregateId != payload.MovementId ||
            envelope.StoreId != payload.StoreId ||
            envelope.ActorUserId != payload.ActorUserId)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeCashSync.EnvelopeAuthorityMismatch",
                    "Envelope aggregate/store/actor does not match canonical C6 cash evidence."));
        }

        string canonical =
            CafeCashMovementPostedV1CanonicalJson.Serialize(payload);
        string expectedHash =
            SyncProtocolV1.CreateEnvelope(
                envelope.DeviceId,
                envelope.StoreId,
                envelope.ActorUserId,
                envelope.LocalSequence,
                envelope.OperationType,
                envelope.SchemaVersion,
                envelope.AggregateId,
                envelope.BaseVersion,
                envelope.OccurredAtLocal,
                canonical,
                envelope.OperationId).PayloadHash;

        if (!string.Equals(
                expectedHash,
                envelope.PayloadHash,
                StringComparison.Ordinal))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeCashSync.PayloadHashMismatch",
                    "Envelope payload hash differs from canonical C6 cash evidence."));
        }

        return Result.Success();
    }

    private static Result<bool>? TryReplay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeCashMovementPostedV1 payload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT movement_id,device_id,store_id,actor_user_id,
                   cashier_session_id,movement_type,
                   requested_amount_cents,delta_cents,
                   reference_type,reference_id,reversal_of_movement_id,
                   reason,payload_hash,occurred_at_utc
            FROM cafe_cash_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            envelope.OperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        int expectedType =
            Convert.ToInt32(ToMovementType(payload.MovementType));
        string occurred =
            payload.OccurredAtUtc.ToUniversalTime().ToString("O");

        bool exact =
            Guid.Parse(reader.GetString(0)) == payload.MovementId &&
            Guid.Parse(reader.GetString(1)) == envelope.DeviceId &&
            Guid.Parse(reader.GetString(2)) == payload.StoreId &&
            Guid.Parse(reader.GetString(3)) == payload.ActorUserId &&
            Guid.Parse(reader.GetString(4)) == payload.CashierSessionId &&
            reader.GetInt32(5) == expectedType &&
            reader.GetInt64(6) == payload.RequestedAmountCents &&
            reader.GetInt64(7) == payload.DeltaCents &&
            string.Equals(reader.GetString(8), payload.ReferenceType, StringComparison.Ordinal) &&
            Guid.Parse(reader.GetString(9)) == payload.ReferenceId &&
            NullableGuidMatches(reader, 10, payload.ReversalOfMovementId) &&
            string.Equals(reader.GetString(11), payload.Reason, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(12), envelope.PayloadHash, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(13), occurred, StringComparison.Ordinal);

        return exact
            ? Result<bool>.Success(false)
            : Result<bool>.Fail(
                Error.Conflict(
                    "CafeCashSync.OperationCollision",
                    "Operation ID already exists with different C6 cash evidence."));
    }

    private static Result ValidateInventoryPurchaseReference(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid purchaseReceiptId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM purchase_receipts
            WHERE purchase_receipt_id=$purchase
              AND store_id=$store;
            """;
        command.Parameters.AddWithValue(
            "$purchase",
            purchaseReceiptId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeCashSync.InventoryPurchaseSourceMissing",
                    "InventoryPurchase movement requires the accepted C3 purchase receipt."));
    }

    private static Result ValidateReversalSource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeCashMovementPostedV1 payload)
    {
        if (!payload.ReversalOfMovementId.HasValue ||
            payload.ReferenceId != payload.ReversalOfMovementId.Value)
        {
            return Result.Fail(
                Error.Validation(
                    "CafeCashSync.ReversalReferenceInvalid",
                    "C6 reversal must reference the exact original movement."));
        }

        using (SqliteCommand source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
                SELECT m.cashier_session_id,m.delta_cents,m.movement_type
                FROM cash_movements m
                INNER JOIN cafe_cash_operations o
                  ON o.movement_id=m.id
                WHERE m.id=$original
                  AND m.movement_type IN ($cashIn,$purchase,$expense,$cashOut)
                LIMIT 1;
                """;
            source.Parameters.AddWithValue(
                "$original",
                payload.ReversalOfMovementId.Value.ToString("D"));
            source.Parameters.AddWithValue(
                "$cashIn",
                Convert.ToInt32(CashMovementType.CashIn));
            source.Parameters.AddWithValue(
                "$purchase",
                Convert.ToInt32(CashMovementType.InventoryPurchase));
            source.Parameters.AddWithValue(
                "$expense",
                Convert.ToInt32(CashMovementType.OperationalExpense));
            source.Parameters.AddWithValue(
                "$cashOut",
                Convert.ToInt32(CashMovementType.CashOut));

            using SqliteDataReader reader = source.ExecuteReader();
            if (!reader.Read())
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeCashSync.ReversalSourceInvalid",
                        "C6 reversal source is missing or not a C6-owned manual movement."));
            }

            Guid sessionId = Guid.Parse(reader.GetString(0));
            long originalDelta = reader.GetInt64(1);
            if (sessionId != payload.CashierSessionId ||
                payload.DeltaCents != checked(-originalDelta) ||
                payload.RequestedAmountCents != Math.Abs(originalDelta))
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeCashSync.ReversalMismatch",
                        "C6 reversal does not exactly compensate the original movement in the same cashier session."));
            }
        }

        using SqliteCommand duplicate = connection.CreateCommand();
        duplicate.Transaction = transaction;
        duplicate.CommandText = """
            SELECT COUNT(*)
            FROM cash_movements
            WHERE reversal_of_movement_id=$original;
            """;
        duplicate.Parameters.AddWithValue(
            "$original",
            payload.ReversalOfMovementId.Value.ToString("D"));

        return Convert.ToInt64(duplicate.ExecuteScalar() ?? 0L) == 0
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeCashSync.DuplicateReversal",
                    "Original C6 movement already has a compensating reversal."));
    }

    private static void InsertOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeCashMovementPostedV1 payload,
        CashMovementType movementType)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_cash_operations(
                operation_id,movement_id,device_id,store_id,
                actor_user_id,cashier_session_id,movement_type,
                requested_amount_cents,delta_cents,
                reference_type,reference_id,
                reversal_of_movement_id,reason,payload_hash,
                occurred_at_utc)
            VALUES(
                $operation,$movement,$device,$store,
                $actor,$session,$type,
                $amount,$delta,
                $referenceType,$referenceId,
                $reversal,$reason,$hash,$occurred);
            """;
        command.Parameters.AddWithValue(
            "$operation",
            envelope.OperationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$movement",
            payload.MovementId.ToString("D"));
        command.Parameters.AddWithValue(
            "$device",
            envelope.DeviceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            payload.StoreId.ToString("D"));
        command.Parameters.AddWithValue(
            "$actor",
            payload.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$session",
            payload.CashierSessionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$type",
            Convert.ToInt32(movementType));
        command.Parameters.AddWithValue(
            "$amount",
            payload.RequestedAmountCents);
        command.Parameters.AddWithValue(
            "$delta",
            payload.DeltaCents);
        command.Parameters.AddWithValue(
            "$referenceType",
            payload.ReferenceType);
        command.Parameters.AddWithValue(
            "$referenceId",
            payload.ReferenceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$reversal",
            payload.ReversalOfMovementId.HasValue
                ? payload.ReversalOfMovementId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$reason",
            payload.Reason);
        command.Parameters.AddWithValue(
            "$hash",
            envelope.PayloadHash);
        command.Parameters.AddWithValue(
            "$occurred",
            payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool MovementExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid movementId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM cash_movements WHERE id=$id;";
        command.Parameters.AddWithValue(
            "$id",
            movementId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static bool NullableGuidMatches(
        SqliteDataReader reader,
        int ordinal,
        Guid? expected)
    {
        if (!expected.HasValue)
            return reader.IsDBNull(ordinal);

        return !reader.IsDBNull(ordinal) &&
               Guid.Parse(reader.GetString(ordinal)) == expected.Value;
    }

    private static CashMovementType ToMovementType(string movementType) =>
        movementType switch
        {
            CafeCashMovementSyncContract.CashIn =>
                CashMovementType.CashIn,
            CafeCashMovementSyncContract.CashOut =>
                CashMovementType.CashOut,
            CafeCashMovementSyncContract.OperationalExpense =>
                CashMovementType.OperationalExpense,
            CafeCashMovementSyncContract.InventoryPurchase =>
                CashMovementType.InventoryPurchase,
            CafeCashMovementSyncContract.Reversal =>
                CashMovementType.Reversal,
            _ => throw new InvalidOperationException(
                "Unsupported C6 cash movement type.")
        };

    private static string RequiredPermission(string movementType) =>
        movementType switch
        {
            CafeCashMovementSyncContract.CashIn =>
                SalrynPermissions.CashIn,
            CafeCashMovementSyncContract.CashOut =>
                SalrynPermissions.CashOut,
            CafeCashMovementSyncContract.OperationalExpense =>
                SalrynPermissions.ExpensesRecord,
            CafeCashMovementSyncContract.InventoryPurchase =>
                SalrynPermissions.CashOut,
            CafeCashMovementSyncContract.Reversal =>
                SalrynPermissions.CashReverse,
            _ => throw new InvalidOperationException(
                "Unsupported C6 cash movement type.")
        };
}
