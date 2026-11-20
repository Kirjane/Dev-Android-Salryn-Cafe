using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCafeCashService :
    SqliteRepositoryBase,
    ICafeCashService
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeCashService(
        SqliteConnectionFactory connectionFactory)
        : base(
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory)))
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
    }

    public Result<CafeCashMutationResult> Post(
        CafeCashPostMutation mutation)
    {
        string? validation = ValidatePostMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeCashMutationResult>.Fail(
                Error.Validation(
                    "CafeCash.PostInvalid",
                    validation));
        }

        string reason = mutation.Reason.Trim();
        string referenceType = mutation.ReferenceType.Trim();
        long delta = ResolveDelta(
            mutation.MovementType,
            mutation.AmountCents);

        CafeCashMovementPostedV1 payload = new(
            mutation.MovementId,
            mutation.StoreId,
            mutation.CashierSessionId,
            mutation.ActorUserId,
            ToSyncMovementType(mutation.MovementType),
            mutation.AmountCents,
            delta,
            referenceType,
            mutation.ReferenceId,
            null,
            reason,
            mutation.OccurredAtUtc.ToUniversalTime());

        string payloadJson =
            CafeCashMovementPostedV1CanonicalJson.Serialize(payload);
        string payloadHash = Hash(payloadJson);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                RequiredPermission(mutation.MovementType));
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result session = ValidateSession(
                connection,
                transaction,
                mutation.CashierSessionId,
                mutation.ActorUserId);
            if (session.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    session.ErrorDetail);
            }

            Result<CafeCashMutationResult>? replay =
                TryReplay(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeviceId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (MovementIdExists(
                    connection,
                    transaction,
                    mutation.MovementId))
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    Error.Conflict(
                        "CafeCash.MovementIdConflict",
                        "Cash movement ID already exists without matching operation evidence."));
            }

            Result reference = ValidateReference(
                connection,
                transaction,
                mutation.MovementType,
                mutation.StoreId,
                referenceType,
                mutation.ReferenceId);
            if (reference.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    reference.ErrorDetail);
            }

            CashMovement movement = new()
            {
                Id = mutation.MovementId,
                CashierSessionId = mutation.CashierSessionId,
                ActorUserId = mutation.ActorUserId,
                MovementType = mutation.MovementType,
                Delta = Money.FromCents(delta),
                ReferenceType = referenceType,
                ReferenceId = mutation.ReferenceId,
                ReversalOfMovementId = null,
                Reason = reason,
                CreatedAt = mutation.OccurredAtUtc.ToUniversalTime()
            };

            Result ledger = SqliteCashMovementPersistence.Insert(
                connection,
                transaction,
                movement);
            if (ledger.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    ledger.ErrorDetail);
            }

            InsertOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.MovementId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.CashierSessionId,
                mutation.MovementType,
                mutation.AmountCents,
                delta,
                referenceType,
                mutation.ReferenceId,
                null,
                reason,
                payloadHash,
                mutation.OccurredAtUtc);

            Enqueue(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.MovementId,
                mutation.OccurredAtUtc,
                payloadJson);

            transaction.Commit();

            return Result<CafeCashMutationResult>.Success(
                new CafeCashMutationResult(
                    mutation.OperationId,
                    mutation.MovementId,
                    mutation.CashierSessionId,
                    mutation.MovementType,
                    delta,
                    false));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeCashMutationResult>.Fail(
                Error.Conflict(
                    "CafeCash.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            OverflowException or
            FormatException or
            ArgumentException)
        {
            return Unexpected<CafeCashMutationResult>(
                "CafeCash.PostFailed",
                ex);
        }
    }

    public Result<CafeCashMutationResult> Reverse(
        CafeCashReversalMutation mutation)
    {
        string? validation = ValidateReversalMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeCashMutationResult>.Fail(
                Error.Validation(
                    "CafeCash.ReversalInvalid",
                    validation));
        }

        string reason = mutation.Reason.Trim();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.CashReverse);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result session = ValidateSession(
                connection,
                transaction,
                mutation.CashierSessionId,
                mutation.ActorUserId);
            if (session.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    session.ErrorDetail);
            }

            OriginalMovement? original = ReadEligibleOriginal(
                connection,
                transaction,
                mutation.OriginalMovementId);

            if (original is null)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    Error.Conflict(
                        "CafeCash.ReversalSourceInvalid",
                        "Reversal source must be an existing C6-owned manual cash movement."));
            }

            if (original.StoreId != mutation.StoreId ||
                original.CashierSessionId != mutation.CashierSessionId)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    Error.Conflict(
                        "CafeCash.ReversalAuthorityMismatch",
                        "Reversal must retain original store and cashier-session authority."));
            }

            long amount = checked(Math.Abs(original.DeltaCents));
            long delta = checked(-original.DeltaCents);
            const string referenceType =
                CafeCashReferenceTypes.Reversal;

            CafeCashMovementPostedV1 payload = new(
                mutation.MovementId,
                mutation.StoreId,
                mutation.CashierSessionId,
                mutation.ActorUserId,
                CafeCashMovementSyncContract.Reversal,
                amount,
                delta,
                referenceType,
                mutation.OriginalMovementId,
                mutation.OriginalMovementId,
                reason,
                mutation.OccurredAtUtc.ToUniversalTime());

            string payloadJson =
                CafeCashMovementPostedV1CanonicalJson.Serialize(payload);
            string payloadHash = Hash(payloadJson);

            Result<CafeCashMutationResult>? replay =
                TryReplay(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeviceId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (MovementIdExists(
                    connection,
                    transaction,
                    mutation.MovementId))
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    Error.Conflict(
                        "CafeCash.MovementIdConflict",
                        "Reversal movement ID already exists without matching operation evidence."));
            }

            if (HasReversal(
                    connection,
                    transaction,
                    mutation.OriginalMovementId))
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    Error.Conflict(
                        "CafeCash.DuplicateReversal",
                        "Original cash movement already has a compensating reversal."));
            }

            CashMovement reversal = new()
            {
                Id = mutation.MovementId,
                CashierSessionId = mutation.CashierSessionId,
                ActorUserId = mutation.ActorUserId,
                MovementType = CashMovementType.Reversal,
                Delta = Money.FromCents(delta),
                ReferenceType = referenceType,
                ReferenceId = mutation.OriginalMovementId,
                ReversalOfMovementId = mutation.OriginalMovementId,
                Reason = reason,
                CreatedAt = mutation.OccurredAtUtc.ToUniversalTime()
            };

            Result ledger = SqliteCashMovementPersistence.Insert(
                connection,
                transaction,
                reversal);
            if (ledger.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeCashMutationResult>.Fail(
                    ledger.ErrorDetail);
            }

            InsertOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.MovementId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.CashierSessionId,
                CashMovementType.Reversal,
                amount,
                delta,
                referenceType,
                mutation.OriginalMovementId,
                mutation.OriginalMovementId,
                reason,
                payloadHash,
                mutation.OccurredAtUtc);

            Enqueue(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.MovementId,
                mutation.OccurredAtUtc,
                payloadJson);

            transaction.Commit();

            return Result<CafeCashMutationResult>.Success(
                new CafeCashMutationResult(
                    mutation.OperationId,
                    mutation.MovementId,
                    mutation.CashierSessionId,
                    CashMovementType.Reversal,
                    delta,
                    false));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeCashMutationResult>.Fail(
                Error.Conflict(
                    "CafeCash.ReversalConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            OverflowException or
            FormatException or
            ArgumentException)
        {
            return Unexpected<CafeCashMutationResult>(
                "CafeCash.ReversalFailed",
                ex);
        }
    }

    public Result<IReadOnlyList<CafeCashMovementSnapshot>>
        GetSessionMovements(
            Guid storeId,
            Guid actorUserId,
            Guid cashierSessionId)
    {
        if (storeId == Guid.Empty ||
            actorUserId == Guid.Empty ||
            cashierSessionId == Guid.Empty)
        {
            return Result<IReadOnlyList<CafeCashMovementSnapshot>>.Fail(
                Error.Validation(
                    "CafeCash.HistoryIdentityInvalid",
                    "Store, actor and cashier session are required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.CashView);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<IReadOnlyList<CafeCashMovementSnapshot>>.Fail(
                    authority.ErrorDetail);
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id,cashier_session_id,actor_user_id,
                       movement_type,delta_cents,reference_type,
                       reference_id,reversal_of_movement_id,
                       reason,created_at_utc
                FROM cash_movements
                WHERE cashier_session_id=$session
                ORDER BY created_at_utc,id;
                """;
            command.Parameters.AddWithValue(
                "$session",
                cashierSessionId.ToString("D"));

            List<CafeCashMovementSnapshot> rows = new();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new CafeCashMovementSnapshot(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    (CashMovementType)reader.GetInt32(3),
                    reader.GetInt64(4),
                    reader.GetString(5),
                    Guid.Parse(reader.GetString(6)),
                    reader.IsDBNull(7)
                        ? null
                        : Guid.Parse(reader.GetString(7)),
                    reader.IsDBNull(8)
                        ? null
                        : reader.GetString(8),
                    DateTimeOffset.Parse(
                        reader.GetString(9),
                        CultureInfo.InvariantCulture)));
            }

            transaction.Commit();

            return Result<IReadOnlyList<CafeCashMovementSnapshot>>.Success(
                rows);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Unexpected<IReadOnlyList<CafeCashMovementSnapshot>>(
                "CafeCash.HistoryFailed",
                ex);
        }
    }

    private Result<CafeCashMutationResult>? TryReplay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT movement_id,device_id,cashier_session_id,
                   movement_type,delta_cents,payload_hash
            FROM cafe_cash_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        Guid movementId = Guid.Parse(reader.GetString(0));
        Guid retainedDevice = Guid.Parse(reader.GetString(1));
        Guid cashierSessionId = Guid.Parse(reader.GetString(2));
        CashMovementType movementType =
            (CashMovementType)reader.GetInt32(3);
        long delta = reader.GetInt64(4);
        string retainedHash = reader.GetString(5);
        reader.Close();

        if (retainedDevice != deviceId ||
            !string.Equals(
                retainedHash,
                payloadHash,
                StringComparison.Ordinal))
        {
            return Result<CafeCashMutationResult>.Fail(
                Error.Conflict(
                    "CafeCash.OperationPayloadConflict",
                    "Operation ID already exists with different cash movement evidence."));
        }

        if (_outbox.Find(
                connection,
                transaction,
                operationId) is null)
        {
            return Result<CafeCashMutationResult>.Fail(
                Error.Unexpected(
                    "CafeCash.ReplayOutboxMissing",
                    "Retained C6 cash operation is missing its atomic sync outbox evidence."));
        }

        return Result<CafeCashMutationResult>.Success(
            new CafeCashMutationResult(
                operationId,
                movementId,
                cashierSessionId,
                movementType,
                delta,
                true));
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId,
        string permission)
    {
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
                storeId.ToString("D"));

            if (Convert.ToInt64(
                    store.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeCash.StoreAuthorityMismatch",
                        "Cash StoreId does not match local deployment authority."));
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$user
              AND u.status=1
              AND rp.permission=$permission;
            """;
        command.Parameters.AddWithValue(
            "$user",
            actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$permission",
            permission);

        if (Convert.ToInt64(
                command.ExecuteScalar() ?? 0L) != 1)
        {
            return Result.Fail(
                Error.Forbidden(
                    "CafeCash.PermissionRequired",
                    "Cash operation requires permission: " +
                    permission));
        }

        return Result.Success();
    }

    private static Result ValidateSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid cashierSessionId,
        Guid actorUserId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cashier_sessions
            WHERE id=$session
              AND user_id=$actor
              AND status=$open;
            """;
        command.Parameters.AddWithValue(
            "$session",
            cashierSessionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$actor",
            actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$open",
            Convert.ToInt32(CashierSessionStatus.Open));

        return Convert.ToInt64(
                   command.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeCash.OpenCashierSessionRequired",
                    "Cash operation requires the actor's exact open cashier session."));
    }

    private static Result ValidateReference(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CashMovementType movementType,
        Guid storeId,
        string referenceType,
        Guid referenceId)
    {
        string requiredType = movementType switch
        {
            CashMovementType.CashIn =>
                CafeCashReferenceTypes.CashIn,
            CashMovementType.CashOut =>
                CafeCashReferenceTypes.CashOut,
            CashMovementType.OperationalExpense =>
                CafeCashReferenceTypes.OperationalExpense,
            CashMovementType.InventoryPurchase =>
                CafeCashReferenceTypes.InventoryPurchase,
            _ => string.Empty
        };

        if (!string.Equals(
                referenceType,
                requiredType,
                StringComparison.Ordinal))
        {
            return Result.Fail(
                Error.Validation(
                    "CafeCash.ReferenceTypeInvalid",
                    "Cash movement reference type does not match movement class."));
        }

        if (movementType != CashMovementType.InventoryPurchase)
        {
            return Result.Success();
        }

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
            referenceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        return Convert.ToInt64(
                   command.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeCash.InventoryPurchaseSourceMissing",
                    "InventoryPurchase cash movement must reference an accepted C3 purchase receipt for this store."));
    }

    private void Enqueue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        Guid movementId,
        DateTimeOffset occurredAtUtc,
        string payloadJson)
    {
        CafeCashMovementCanonicalParseResult parsed =
            CafeCashMovementPostedV1CanonicalJson.Parse(
                CafeCashMovementSyncContract.OperationType,
                payloadJson);
        if (!parsed.IsValid)
        {
            throw new InvalidOperationException(
                (parsed.ErrorCode ??
                 "CafeCashSync.PayloadInvalid") +
                ": " +
                (parsed.ErrorMessage ??
                 "Cash sync payload is invalid."));
        }

        _outbox.Enqueue(
            connection,
            transaction,
            new SyncEnqueueRequest(
                deviceId,
                storeId,
                actorUserId,
                CafeCashMovementSyncContract.OperationType,
                CafeCashMovementSyncContract.SchemaVersion,
                movementId,
                null,
                occurredAtUtc,
                payloadJson,
                operationId));
    }

    private static void InsertOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid movementId,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        Guid cashierSessionId,
        CashMovementType movementType,
        long requestedAmountCents,
        long deltaCents,
        string referenceType,
        Guid referenceId,
        Guid? reversalOfMovementId,
        string reason,
        string payloadHash,
        DateTimeOffset occurredAtUtc)
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
            operationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$movement",
            movementId.ToString("D"));
        command.Parameters.AddWithValue(
            "$device",
            deviceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        command.Parameters.AddWithValue(
            "$actor",
            actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$session",
            cashierSessionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$type",
            Convert.ToInt32(movementType));
        command.Parameters.AddWithValue(
            "$amount",
            requestedAmountCents);
        command.Parameters.AddWithValue(
            "$delta",
            deltaCents);
        command.Parameters.AddWithValue(
            "$referenceType",
            referenceType);
        command.Parameters.AddWithValue(
            "$referenceId",
            referenceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$reversal",
            reversalOfMovementId.HasValue
                ? reversalOfMovementId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$reason",
            reason);
        command.Parameters.AddWithValue(
            "$hash",
            payloadHash);
        command.Parameters.AddWithValue(
            "$occurred",
            occurredAtUtc
                .ToUniversalTime()
                .ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool MovementIdExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid movementId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cash_movements
            WHERE id=$movement;
            """;
        command.Parameters.AddWithValue(
            "$movement",
            movementId.ToString("D"));
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L) != 0;
    }

    private static bool HasReversal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid originalMovementId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cash_movements
            WHERE reversal_of_movement_id=$original;
            """;
        command.Parameters.AddWithValue(
            "$original",
            originalMovementId.ToString("D"));
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L) != 0;
    }

    private static OriginalMovement? ReadEligibleOriginal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid movementId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT o.store_id,m.cashier_session_id,
                   m.movement_type,m.delta_cents
            FROM cash_movements m
            INNER JOIN cafe_cash_operations o
              ON o.movement_id=m.id
            WHERE m.id=$movement
              AND m.movement_type IN ($cashIn,$purchase,$expense,$cashOut)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$movement",
            movementId.ToString("D"));
        command.Parameters.AddWithValue(
            "$cashIn",
            Convert.ToInt32(CashMovementType.CashIn));
        command.Parameters.AddWithValue(
            "$purchase",
            Convert.ToInt32(CashMovementType.InventoryPurchase));
        command.Parameters.AddWithValue(
            "$expense",
            Convert.ToInt32(CashMovementType.OperationalExpense));
        command.Parameters.AddWithValue(
            "$cashOut",
            Convert.ToInt32(CashMovementType.CashOut));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new OriginalMovement(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            (CashMovementType)reader.GetInt32(2),
            reader.GetInt64(3));
    }

    private static string? ValidatePostMutation(
        CafeCashPostMutation mutation)
    {
        if (mutation is null)
            return "Cash post mutation is required.";
        if (mutation.OperationId == Guid.Empty ||
            mutation.MovementId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.CashierSessionId == Guid.Empty ||
            mutation.ReferenceId == Guid.Empty)
        {
            return "Cash post identities must be non-empty.";
        }

        if (mutation.AmountCents <= 0)
            return "Cash post amount must be positive.";
        if (string.IsNullOrWhiteSpace(mutation.Reason))
            return "Cash post reason is required.";
        if (string.IsNullOrWhiteSpace(mutation.ReferenceType))
            return "Cash post reference type is required.";

        if (CafeBookingCashReferenceTypes.IsReserved(
                mutation.ReferenceType))
        {
            return "Booking deposit cash references are reserved for C9 booking authority.";
        }

        if (mutation.MovementType is not (
                CashMovementType.CashIn or
                CashMovementType.CashOut or
                CashMovementType.OperationalExpense or
                CashMovementType.InventoryPurchase))
        {
            return "C6 manual post supports only CashIn, CashOut, OperationalExpense and InventoryPurchase.";
        }

        return null;
    }

    private static string? ValidateReversalMutation(
        CafeCashReversalMutation mutation)
    {
        if (mutation is null)
            return "Cash reversal mutation is required.";
        if (mutation.OperationId == Guid.Empty ||
            mutation.MovementId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.CashierSessionId == Guid.Empty ||
            mutation.OriginalMovementId == Guid.Empty)
        {
            return "Cash reversal identities must be non-empty.";
        }

        if (mutation.MovementId == mutation.OriginalMovementId)
            return "Reversal movement ID must differ from original movement ID.";
        if (string.IsNullOrWhiteSpace(mutation.Reason))
            return "Cash reversal reason is required.";

        return null;
    }

    private static long ResolveDelta(
        CashMovementType movementType,
        long amountCents) =>
        movementType == CashMovementType.CashIn
            ? amountCents
            : checked(-amountCents);

    private static string RequiredPermission(
        CashMovementType movementType) =>
        movementType switch
        {
            CashMovementType.CashIn =>
                SalrynPermissions.CashIn,
            CashMovementType.CashOut =>
                SalrynPermissions.CashOut,
            CashMovementType.OperationalExpense =>
                SalrynPermissions.ExpensesRecord,
            CashMovementType.InventoryPurchase =>
                SalrynPermissions.CashOut,
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(movementType))
        };

    private static string ToSyncMovementType(
        CashMovementType movementType) =>
        movementType switch
        {
            CashMovementType.CashIn =>
                CafeCashMovementSyncContract.CashIn,
            CashMovementType.CashOut =>
                CafeCashMovementSyncContract.CashOut,
            CashMovementType.OperationalExpense =>
                CafeCashMovementSyncContract.OperationalExpense,
            CashMovementType.InventoryPurchase =>
                CafeCashMovementSyncContract.InventoryPurchase,
            CashMovementType.Reversal =>
                CafeCashMovementSyncContract.Reversal,
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(movementType))
        };

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record OriginalMovement(
        Guid StoreId,
        Guid CashierSessionId,
        CashMovementType MovementType,
        long DeltaCents);
}
