using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private const string CafeOrderProjectionSavepoint = "c4_order_projection";

    private static bool IsCafeOrderAppliedChange(
        AppliedBusinessChangeV1 change) =>
        change.SchemaVersion ==
            AppliedBusinessChangeContract.CafeOrderAppliedSchemaVersion &&
        (
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderRevisionCommittedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderParkedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderResumedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderPreparationSubmittedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderPreparationStatusChangedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderCancelledV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.OrderCompletedV1AppliedV1,
                StringComparison.Ordinal)
        );

    private Result<SyncProjectionResult> ProjectCafeOrder(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        string? operationType =
            OperationTypeForAppliedChange(change.ChangeType);
        if (operationType is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeOrder.ProjectionContractUnsupported",
                "Applied change is not a supported C4 order type.");
        }

        CafeOrderCanonicalParseResult parsed =
            CafeOrderOperationCanonicalJson.Parse(
                operationType,
                change.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parsed.ErrorCode ?? "CafeOrder.ProjectionPayloadInvalid",
                parsed.ErrorMessage ?? "Canonical C4 order payload could not be parsed.");
        }

        CafeOrderOperationV1 payload = parsed.Payload;
        if (payload.OrderId != change.AggregateId ||
            payload.StoreId != storeId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeOrder.ProjectionAggregateMismatch",
                "C4 order payload identity does not match applied change/store authority.");
        }

        bool originEcho =
            change.OriginDeviceId == localDeviceId;
        if (originEcho)
        {
            Result outbox = VerifyCafeOrderOriginOutbox(
                context.Connection,
                context.Transaction,
                change,
                operationType);
            if (outbox.IsFailure)
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    outbox.ErrorDetail.Code,
                    outbox.ErrorDetail.Message);
            }

            if (!SqliteCafeOrderStore.VerifyCanonicalOperation(
                    context.Connection,
                    context.Transaction,
                    change.SourceOperationId,
                    payload))
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    "CafeOrder.OriginFactMismatch",
                    "Origin echo conflicts with retained C4 order operation evidence.");
            }

            _failureInjector.ThrowIfRequested(
                SyncProjectionFailurePoint.AfterDomainWriteBeforeResolution,
                expectedSequence);

            ResolveApplied(
                context.Connection,
                context.Transaction,
                storeId,
                state,
                expectedSequence,
                DateTimeOffset.UtcNow);

            SyncRemoteState resolved =
                RequireRemoteState(context, storeId);
            return Result<SyncProjectionResult>.Success(
                SyncProjectionResult.Applied(
                    expectedSequence,
                    isOriginEcho: true,
                    resolved,
                    SyncProjectionContract.OriginEchoResolvedCode));
        }

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.BeforeDomainWrite,
            expectedSequence);

        ExecCafeOrderProjection(
            context.Connection,
            context.Transaction,
            "SAVEPOINT " + CafeOrderProjectionSavepoint + ";");

        Result applied =
            SqliteCafeOrderStore.ApplyCanonical(
                context.Connection,
                context.Transaction,
                change.SourceOperationId,
                payload);

        if (applied.IsFailure)
        {
            ExecCafeOrderProjection(
                context.Connection,
                context.Transaction,
                "ROLLBACK TO " + CafeOrderProjectionSavepoint + ";");
            ExecCafeOrderProjection(
                context.Connection,
                context.Transaction,
                "RELEASE " + CafeOrderProjectionSavepoint + ";");

            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                applied.ErrorDetail.Code,
                applied.ErrorDetail.Message);
        }

        ExecCafeOrderProjection(
            context.Connection,
            context.Transaction,
            "RELEASE " + CafeOrderProjectionSavepoint + ";");

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.AfterDomainWriteBeforeResolution,
            expectedSequence);

        ResolveApplied(
            context.Connection,
            context.Transaction,
            storeId,
            state,
            expectedSequence,
            DateTimeOffset.UtcNow);

        SyncRemoteState finalState =
            RequireRemoteState(context, storeId);
        return Result<SyncProjectionResult>.Success(
            SyncProjectionResult.Applied(
                expectedSequence,
                isOriginEcho: false,
                finalState,
                SyncProjectionContract.ProjectionAppliedCode));
    }

    private static Result VerifyCafeOrderOriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        string operationType)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT device_id,store_id,local_sequence,operation_type,schema_version,
                   aggregate_id,base_version,payload_json,payload_hash
            FROM sync_outbox
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            change.SourceOperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeOrder.OriginOutboxMissing",
                    "Origin echo has no matching durable C4 outbox evidence."));
        }

        bool matches =
            Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
            Guid.Parse(reader.GetString(1)) == change.StoreId &&
            reader.GetInt64(2) == change.OriginLocalSequence &&
            string.Equals(reader.GetString(3), operationType, StringComparison.Ordinal) &&
            reader.GetInt32(4) == CafeOrderOperationContract.SchemaVersion &&
            Guid.Parse(reader.GetString(5)) == change.AggregateId &&
            !reader.IsDBNull(6) &&
            string.Equals(reader.GetString(7), change.PayloadJson, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(8), change.PayloadHash, StringComparison.Ordinal);

        return matches
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeOrder.OriginOutboxMismatch",
                    "Origin echo conflicts with retained C4 outbox evidence."));
    }

    private static string? OperationTypeForAppliedChange(
        string changeType) =>
        changeType switch
        {
            AppliedBusinessChangeContract.OrderRevisionCommittedV1AppliedV1 =>
                CafeOrderOperationContract.OrderRevisionCommittedV1,
            AppliedBusinessChangeContract.OrderParkedV1AppliedV1 =>
                CafeOrderOperationContract.OrderParkedV1,
            AppliedBusinessChangeContract.OrderResumedV1AppliedV1 =>
                CafeOrderOperationContract.OrderResumedV1,
            AppliedBusinessChangeContract.OrderPreparationSubmittedV1AppliedV1 =>
                CafeOrderOperationContract.OrderPreparationSubmittedV1,
            AppliedBusinessChangeContract.OrderPreparationStatusChangedV1AppliedV1 =>
                CafeOrderOperationContract.OrderPreparationStatusChangedV1,
            AppliedBusinessChangeContract.OrderCancelledV1AppliedV1 =>
                CafeOrderOperationContract.OrderCancelledV1,
            AppliedBusinessChangeContract.OrderCompletedV1AppliedV1 =>
                CafeOrderOperationContract.OrderCompletedV1,
            _ => null
        };

    private static void ExecCafeOrderProjection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
