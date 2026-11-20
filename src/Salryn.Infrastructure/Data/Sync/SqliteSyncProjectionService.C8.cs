using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private const string CafeDeliveryProjectionSavepoint = "c8_delivery_projection";

    private static bool IsCafeDeliveryAppliedChange(
        AppliedBusinessChangeV1 change) =>
        change.SchemaVersion ==
            AppliedBusinessChangeContract.CafeDeliveryAppliedSchemaVersion &&
        (
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.DeliveryConfiguredV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.DeliveryDispatchedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.DeliveryResolvedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.DeliveryCancelledV1AppliedV1,
                StringComparison.Ordinal)
        );

    private Result<SyncProjectionResult> ProjectCafeDelivery(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        string? operationType =
            DeliveryOperationTypeForAppliedChange(change.ChangeType);
        if (operationType is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeDeliverySync.ProjectionContractUnsupported",
                "Applied change is not a supported C8 delivery type.");
        }

        CafeDeliveryCanonicalParseResult parsed =
            CafeDeliveryOperationCanonicalJson.Parse(
                operationType,
                change.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parsed.ErrorCode ?? "CafeDeliverySync.ProjectionPayloadInvalid",
                parsed.ErrorMessage ??
                    "Canonical C8 delivery payload could not be parsed.");
        }

        CafeDeliveryOperationV1 payload = parsed.Payload;
        if (payload.OperationId != change.SourceOperationId ||
            payload.DeliveryId != change.AggregateId ||
            payload.StoreId != storeId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeDeliverySync.ProjectionAggregateMismatch",
                "C8 delivery payload identity does not match applied change/store authority.");
        }

        SyncOperationEnvelopeV1 envelope = SyncProtocolV1.CreateEnvelope(
            change.OriginDeviceId,
            change.StoreId,
            payload.ActorUserId,
            change.OriginLocalSequence,
            operationType,
            CafeDeliverySyncContract.SchemaVersion,
            change.AggregateId,
            payload.ExpectedVersion,
            payload.OccurredAtUtc,
            change.PayloadJson,
            change.SourceOperationId);

        bool originEcho = change.OriginDeviceId == localDeviceId;
        if (originEcho)
        {
            Result outbox = VerifyCafeDeliveryOriginOutbox(
                context.Connection,
                context.Transaction,
                change,
                operationType,
                payload);
            if (outbox.IsFailure)
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    outbox.ErrorDetail.Code,
                    outbox.ErrorDetail.Message);
            }

            if (!SqliteCafeDeliverySyncParticipant.VerifyCanonical(
                    context.Connection,
                    context.Transaction,
                    envelope,
                    payload))
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    "CafeDeliverySync.OriginFactMismatch",
                    "Origin echo conflicts with retained C8 delivery operation evidence.");
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

            SyncRemoteState resolved = RequireRemoteState(context, storeId);
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

        ExecCafeDeliveryProjection(
            context.Connection,
            context.Transaction,
            "SAVEPOINT " + CafeDeliveryProjectionSavepoint + ";");

        Result applied = SqliteCafeDeliverySyncParticipant.ApplyCanonical(
            context.Connection,
            context.Transaction,
            envelope,
            payload);

        if (applied.IsFailure)
        {
            ExecCafeDeliveryProjection(
                context.Connection,
                context.Transaction,
                "ROLLBACK TO " + CafeDeliveryProjectionSavepoint + ";");
            ExecCafeDeliveryProjection(
                context.Connection,
                context.Transaction,
                "RELEASE " + CafeDeliveryProjectionSavepoint + ";");

            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                applied.ErrorDetail.Code,
                applied.ErrorDetail.Message);
        }

        ExecCafeDeliveryProjection(
            context.Connection,
            context.Transaction,
            "RELEASE " + CafeDeliveryProjectionSavepoint + ";");

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

        SyncRemoteState finalState = RequireRemoteState(context, storeId);
        return Result<SyncProjectionResult>.Success(
            SyncProjectionResult.Applied(
                expectedSequence,
                isOriginEcho: false,
                finalState,
                SyncProjectionContract.ProjectionAppliedCode));
    }

    private static Result VerifyCafeDeliveryOriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        string operationType,
        CafeDeliveryOperationV1 payload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT device_id,store_id,actor_user_id,local_sequence,
                   operation_type,schema_version,aggregate_id,base_version,
                   payload_json,payload_hash
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
                    "CafeDeliverySync.OriginOutboxMissing",
                    "Origin echo has no matching durable C8 delivery outbox evidence."));
        }

        bool matches =
            Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
            Guid.Parse(reader.GetString(1)) == change.StoreId &&
            Guid.Parse(reader.GetString(2)) == payload.ActorUserId &&
            reader.GetInt64(3) == change.OriginLocalSequence &&
            string.Equals(
                reader.GetString(4),
                operationType,
                StringComparison.Ordinal) &&
            reader.GetInt32(5) == CafeDeliverySyncContract.SchemaVersion &&
            Guid.Parse(reader.GetString(6)) == change.AggregateId &&
            !reader.IsDBNull(7) &&
            reader.GetInt64(7) == payload.ExpectedVersion &&
            string.Equals(
                reader.GetString(8),
                change.PayloadJson,
                StringComparison.Ordinal) &&
            string.Equals(
                reader.GetString(9),
                change.PayloadHash,
                StringComparison.Ordinal);

        return matches
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.OriginOutboxMismatch",
                    "Origin echo conflicts with retained C8 delivery outbox evidence."));
    }

    private static string? DeliveryOperationTypeForAppliedChange(
        string changeType) =>
        changeType switch
        {
            AppliedBusinessChangeContract.DeliveryConfiguredV1AppliedV1 =>
                CafeDeliverySyncContract.DeliveryConfiguredV1,
            AppliedBusinessChangeContract.DeliveryDispatchedV1AppliedV1 =>
                CafeDeliverySyncContract.DeliveryDispatchedV1,
            AppliedBusinessChangeContract.DeliveryResolvedV1AppliedV1 =>
                CafeDeliverySyncContract.DeliveryResolvedV1,
            AppliedBusinessChangeContract.DeliveryCancelledV1AppliedV1 =>
                CafeDeliverySyncContract.DeliveryCancelledV1,
            _ => null
        };

    private static void ExecCafeDeliveryProjection(
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
