using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private static bool IsCafeInventoryAppliedChange(AppliedBusinessChangeV1 change) =>
        change.SchemaVersion == CafeInventoryOperationContract.SchemaVersion &&
        (
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.PurchaseReceiptCommittedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.InventoryWasteCommittedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.InventoryCountFinalizedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.InventoryAdjustmentCommittedV1AppliedV1,
                StringComparison.Ordinal)
        );

    private Result<SyncProjectionResult> ProjectCafeInventory(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        CafeProjectionPayload parsed = ParseCafeInventoryAppliedChange(change);
        if (!parsed.IsValid)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parsed.ErrorCode ?? "CafeInventory.ProjectionPayloadInvalid",
                parsed.ErrorMessage ?? "Canonical C3 payload could not be parsed.");
        }

        if (parsed.AggregateId != change.AggregateId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeInventory.ProjectionAggregateMismatch",
                "C3 factual aggregate does not match applied change aggregate identity.");
        }

        bool originEcho = change.OriginDeviceId == localDeviceId;
        if (originEcho)
        {
            Result outbox = VerifyCafeInventoryOriginOutbox(
                context.Connection,
                context.Transaction,
                change,
                parsed.OperationType);

            if (outbox.IsFailure)
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    outbox.ErrorDetail.Code,
                    outbox.ErrorDetail.Message);
            }

            if (!SqliteCafeInventoryStore.VerifyCanonicalFact(
                    context.Connection,
                    context.Transaction,
                    change.SourceOperationId,
                    parsed.OperationType,
                    change.PayloadJson))
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    "CafeInventory.OriginFactMismatch",
                    "Origin echo conflicts with retained C3 factual evidence.");
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

        Result applied = ApplyCafeInventoryRemote(
            context.Connection,
            context.Transaction,
            storeId,
            change.SourceOperationId,
            parsed);

        if (applied.IsFailure)
        {
            return Result<SyncProjectionResult>.Fail(applied.ErrorDetail);
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

        SyncRemoteState finalState = RequireRemoteState(context, storeId);
        return Result<SyncProjectionResult>.Success(
            SyncProjectionResult.Applied(
                expectedSequence,
                isOriginEcho: false,
                finalState,
                SyncProjectionContract.ProjectionAppliedCode));
    }

    private static CafeProjectionPayload ParseCafeInventoryAppliedChange(
        AppliedBusinessChangeV1 change)
    {
        if (string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.PurchaseReceiptCommittedV1AppliedV1,
                StringComparison.Ordinal))
        {
            CafeInventoryCanonicalParseResult<PurchaseReceiptCommittedV1> parsed =
                CafeInventoryOperationCanonicalJson.ParsePurchase(change.PayloadJson);

            return parsed.IsValid && parsed.Payload is not null
                ? CafeProjectionPayload.Purchase(parsed.Payload)
                : CafeProjectionPayload.Fail(parsed.ErrorCode, parsed.ErrorMessage);
        }

        if (string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.InventoryWasteCommittedV1AppliedV1,
                StringComparison.Ordinal))
        {
            CafeInventoryCanonicalParseResult<InventoryWasteCommittedV1> parsed =
                CafeInventoryOperationCanonicalJson.ParseWaste(change.PayloadJson);

            return parsed.IsValid && parsed.Payload is not null
                ? CafeProjectionPayload.Waste(parsed.Payload)
                : CafeProjectionPayload.Fail(parsed.ErrorCode, parsed.ErrorMessage);
        }

        if (string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.InventoryCountFinalizedV1AppliedV1,
                StringComparison.Ordinal))
        {
            CafeInventoryCanonicalParseResult<InventoryCountFinalizedV1> parsed =
                CafeInventoryOperationCanonicalJson.ParseCount(change.PayloadJson);

            return parsed.IsValid && parsed.Payload is not null
                ? CafeProjectionPayload.Count(parsed.Payload)
                : CafeProjectionPayload.Fail(parsed.ErrorCode, parsed.ErrorMessage);
        }

        if (string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.InventoryAdjustmentCommittedV1AppliedV1,
                StringComparison.Ordinal))
        {
            CafeInventoryCanonicalParseResult<InventoryAdjustmentCommittedV1> parsed =
                CafeInventoryOperationCanonicalJson.ParseAdjustment(change.PayloadJson);

            return parsed.IsValid && parsed.Payload is not null
                ? CafeProjectionPayload.Adjustment(parsed.Payload)
                : CafeProjectionPayload.Fail(parsed.ErrorCode, parsed.ErrorMessage);
        }

        return CafeProjectionPayload.Fail(
            "CafeInventory.ProjectionContractUnsupported",
            "Applied change is not a supported C3 inventory type.");
    }

    private static Result ApplyCafeInventoryRemote(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        CafeProjectionPayload payload)
    {
        return payload.OperationType switch
        {
            CafeInventoryOperationContract.PurchaseReceiptCommittedV1 =>
                SqliteCafeInventoryStore.ApplyCanonicalPurchase(
                    connection,
                    transaction,
                    storeId,
                    operationId,
                    payload.PurchasePayload!),

            CafeInventoryOperationContract.InventoryWasteCommittedV1 =>
                SqliteCafeInventoryStore.ApplyCanonicalWaste(
                    connection,
                    transaction,
                    storeId,
                    operationId,
                    payload.WastePayload!),

            CafeInventoryOperationContract.InventoryCountFinalizedV1 =>
                SqliteCafeInventoryStore.ApplyCanonicalCount(
                    connection,
                    transaction,
                    storeId,
                    operationId,
                    payload.CountPayload!),

            CafeInventoryOperationContract.InventoryAdjustmentCommittedV1 =>
                SqliteCafeInventoryStore.ApplyCanonicalAdjustment(
                    connection,
                    transaction,
                    storeId,
                    operationId,
                    payload.AdjustmentPayload!),

            _ => Result.Fail(Error.Validation(
                "CafeInventory.ProjectionContractUnsupported",
                "Unsupported C3 inventory projection type."))
        };
    }

    private static Result VerifyCafeInventoryOriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        string operationType)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT device_id,store_id,local_sequence,operation_type,schema_version,
                   aggregate_id,payload_json,payload_hash
            FROM sync_outbox
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", change.SourceOperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Result.Fail(Error.Conflict(
                "CafeInventory.OriginOutboxMissing",
                "Origin echo has no matching durable C3 outbox evidence."));
        }

        bool matches =
            Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
            Guid.Parse(reader.GetString(1)) == change.StoreId &&
            reader.GetInt64(2) == change.OriginLocalSequence &&
            string.Equals(reader.GetString(3), operationType, StringComparison.Ordinal) &&
            reader.GetInt32(4) == CafeInventoryOperationContract.SchemaVersion &&
            Guid.Parse(reader.GetString(5)) == change.AggregateId &&
            string.Equals(reader.GetString(6), change.PayloadJson, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(7), change.PayloadHash, StringComparison.Ordinal);

        return matches
            ? Result.Success()
            : Result.Fail(Error.Conflict(
                "CafeInventory.OriginOutboxMismatch",
                "Origin echo conflicts with retained C3 outbox evidence."));
    }

    private sealed record CafeProjectionPayload(
        bool IsValid,
        string OperationType,
        Guid AggregateId,
        PurchaseReceiptCommittedV1? PurchasePayload,
        InventoryWasteCommittedV1? WastePayload,
        InventoryCountFinalizedV1? CountPayload,
        InventoryAdjustmentCommittedV1? AdjustmentPayload,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static CafeProjectionPayload Purchase(PurchaseReceiptCommittedV1 payload) =>
            new(
                true,
                CafeInventoryOperationContract.PurchaseReceiptCommittedV1,
                payload.PurchaseReceiptId,
                payload,
                null,
                null,
                null,
                null,
                null);

        public static CafeProjectionPayload Waste(InventoryWasteCommittedV1 payload) =>
            new(
                true,
                CafeInventoryOperationContract.InventoryWasteCommittedV1,
                payload.WasteEventId,
                null,
                payload,
                null,
                null,
                null,
                null);

        public static CafeProjectionPayload Count(InventoryCountFinalizedV1 payload) =>
            new(
                true,
                CafeInventoryOperationContract.InventoryCountFinalizedV1,
                payload.CountSessionId,
                null,
                null,
                payload,
                null,
                null,
                null);

        public static CafeProjectionPayload Adjustment(InventoryAdjustmentCommittedV1 payload) =>
            new(
                true,
                CafeInventoryOperationContract.InventoryAdjustmentCommittedV1,
                payload.AdjustmentEventId,
                null,
                null,
                null,
                payload,
                null,
                null);

        public static CafeProjectionPayload Fail(string? code, string? message) =>
            new(false, string.Empty, Guid.Empty, null, null, null, null, code, message);
    }
}
