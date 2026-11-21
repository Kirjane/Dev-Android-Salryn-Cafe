using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private const string InjectedFailurePrefix = "Sync.ProjectionInjectedFailure:";
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISqliteSharedTransactionBoundary _transactionBoundary;
    private readonly SharedSqliteCompletedSaleWriteParticipant _saleWriter;
    private readonly SharedSqliteCafeFinancialParticipant _financialWriter;
    private readonly ISyncProjectionFailureInjector _failureInjector;

    public SqliteSyncProjectionService(SqliteConnectionFactory connectionFactory)
        : this(
            connectionFactory,
            new SqliteSharedTransactionBoundary(connectionFactory),
            new SharedSqliteCompletedSaleWriteParticipant(),
            new SharedSqliteCafeFinancialParticipant(),
            NoOpSyncProjectionFailureInjector.Instance)
    {
    }

    public SqliteSyncProjectionService(
        SqliteConnectionFactory connectionFactory,
        ISqliteSharedTransactionBoundary transactionBoundary,
        SharedSqliteCompletedSaleWriteParticipant saleWriter,
        ISyncProjectionFailureInjector failureInjector)
        : this(
            connectionFactory,
            transactionBoundary,
            saleWriter,
            new SharedSqliteCafeFinancialParticipant(),
            failureInjector)
    {
    }

    public SqliteSyncProjectionService(
        SqliteConnectionFactory connectionFactory,
        ISqliteSharedTransactionBoundary transactionBoundary,
        SharedSqliteCompletedSaleWriteParticipant saleWriter,
        SharedSqliteCafeFinancialParticipant financialWriter,
        ISyncProjectionFailureInjector failureInjector)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _transactionBoundary = transactionBoundary ?? throw new ArgumentNullException(nameof(transactionBoundary));
        _saleWriter = saleWriter ?? throw new ArgumentNullException(nameof(saleWriter));
        _financialWriter = financialWriter ?? throw new ArgumentNullException(nameof(financialWriter));
        _failureInjector = failureInjector ?? throw new ArgumentNullException(nameof(failureInjector));
    }

    public async Task<SyncProjectionResult> ProjectNextAsync(
        Guid storeId,
        Guid localDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty || localDeviceId == Guid.Empty)
        {
            return SyncProjectionResult.Fail(
                null,
                null,
                "Sync.ProjectionIdentityRequired",
                "Projection requires non-empty store and local device identities.");
        }

        long? attemptedSequence = null;

        Result<SyncProjectionResult> execution = await _transactionBoundary.ExecuteAsync(
            async (context, token) =>
            {
                SyncRemoteState? state =
                    SqliteSyncPullStore.ReadRemoteState(
                        context.Connection,
                        context.Transaction,
                        storeId);

                if (state is null || !state.CentralInstanceId.HasValue)
                {
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.Reconciliation(
                            null,
                            state,
                            "Sync.ProjectionRemoteStateMissing",
                            "Projection requires explicit bootstrap/remote authority."));
                }

                if (!string.Equals(state.State, AppliedBusinessChangeContract.Ready, StringComparison.Ordinal))
                {
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.Reconciliation(
                            state.LastAppliedServerSequence + 1,
                            state,
                            "Sync.ProjectionRemoteStateNotReady",
                            "Projection is blocked until bootstrap/reconciliation state is resolved."));
                }

                if (state.LastAppliedServerSequence > state.LastReceivedServerSequence)
                {
                    MarkReconciliation(
                        context.Connection,
                        context.Transaction,
                        storeId,
                        null,
                        "Sync.ProjectionCursorInvariantBroken",
                        "Applied cursor cannot exceed received cursor.");

                    SyncRemoteState marked = RequireRemoteState(context, storeId);
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.Reconciliation(
                            null,
                            marked,
                            "Sync.ProjectionCursorInvariantBroken",
                            "Applied cursor cannot exceed received cursor."));
                }

                if (state.LastAppliedServerSequence == state.LastReceivedServerSequence)
                {
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.NoWork(state));
                }

                long expectedSequence = checked(state.LastAppliedServerSequence + 1);
                attemptedSequence = expectedSequence;

                ProjectionInboxRow? row = ReadInboxRow(
                    context.Connection,
                    context.Transaction,
                    storeId,
                    expectedSequence);

                if (row is null)
                {
                    MarkReconciliation(
                        context.Connection,
                        context.Transaction,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionContiguousRowMissing",
                        "Received cursor is ahead but the next contiguous inbox row is missing.");

                    SyncRemoteState marked = RequireRemoteState(context, storeId);
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.Reconciliation(
                            expectedSequence,
                            marked,
                            "Sync.ProjectionContiguousRowMissing",
                            "Received cursor is ahead but the next contiguous inbox row is missing."));
                }

                if (string.Equals(
                        row.ApplyStatus,
                        SyncProjectionContract.ReconciliationRequired,
                        StringComparison.Ordinal))
                {
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.Reconciliation(
                            expectedSequence,
                            state,
                            "Sync.ProjectionPreviouslyUnresolved",
                            row.ApplyError ?? "Earlier projection reconciliation remains unresolved."));
                }

                if (!string.Equals(
                        row.ApplyStatus,
                        AppliedBusinessChangeContract.Received,
                        StringComparison.Ordinal))
                {
                    MarkReconciliation(
                        context.Connection,
                        context.Transaction,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionStatusInvalid",
                        "Next inbox row is not in Received projection state.");

                    SyncRemoteState marked = RequireRemoteState(context, storeId);
                    return Result<SyncProjectionResult>.Success(
                        SyncProjectionResult.Reconciliation(
                            expectedSequence,
                            marked,
                            "Sync.ProjectionStatusInvalid",
                            "Next inbox row is not in Received projection state."));
                }

                AppliedBusinessChangeV1 change = row.Change;
                AppliedBusinessChangeValidationResult changeValidation =
                    AppliedBusinessChangeProtocolV1.Validate(change);

                if (!changeValidation.IsValid)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        changeValidation.ErrorCode ?? "Sync.ProjectionChangeInvalid",
                        changeValidation.ErrorMessage ?? "Inbox change integrity validation failed.");
                }

                if (change.CentralInstanceId != state.CentralInstanceId.Value)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionEpochMismatch",
                        "Inbox change belongs to a different central epoch; rebootstrap/reconciliation is required.");
                }

                if (change.StoreId != storeId)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionStoreMismatch",
                        "Inbox change belongs to a different store.");
                }

                if (string.Equals(
                        change.ChangeType,
                        AppliedBusinessChangeContract.IngredientConsumptionCommittedV1AppliedV1,
                        StringComparison.Ordinal) &&
                    change.SchemaVersion ==
                        AppliedBusinessChangeContract.IngredientConsumptionCommittedV1AppliedSchemaVersion)
                {
                    return ProjectIngredientConsumption(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeShiftAppliedChange(change))
                    return ProjectCafeShift(context, storeId, localDeviceId, state, expectedSequence, change);

                if (IsCafeInventoryAppliedChange(change))
                {
                    return ProjectCafeInventory(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeOrderAppliedChange(change))
                {
                    return ProjectCafeOrder(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeRefundAppliedChange(change))
                {
                    return ProjectCafeRefund(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeCashAppliedChange(change))
                {
                    return ProjectCafeCash(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeDeliveryAppliedChange(change))
                {
                    return ProjectCafeDelivery(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeBookingAppliedChange(change))
                {
                    return ProjectCafeBooking(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsCafeAttendanceIncidentAppliedChange(change))
                {
                    return ProjectCafeAttendanceIncident(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change);
                }

                if (IsSaleCommittedV3AppliedChange(change))
                {
                    return await ProjectSaleCommittedV3Async(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change,
                        token).ConfigureAwait(false);
                }

                if (string.Equals(
                        change.ChangeType,
                        AppliedBusinessChangeContract.SaleCommittedV2AppliedV1,
                        StringComparison.Ordinal) &&
                    change.SchemaVersion ==
                        AppliedBusinessChangeContract.SaleCommittedV2AppliedSchemaVersion)
                {
                    return await ProjectSaleCommittedV2Async(
                        context,
                        storeId,
                        localDeviceId,
                        state,
                        expectedSequence,
                        change,
                        token).ConfigureAwait(false);
                }

                if (!string.Equals(
                        change.ChangeType,
                        AppliedBusinessChangeContract.SaleCommittedV1AppliedV1,
                        StringComparison.Ordinal) ||
                    change.SchemaVersion != AppliedBusinessChangeContract.SaleCommittedV1AppliedSchemaVersion)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionContractUnsupported",
                        "Inbox change type/schema is not supported by projection.");
                }

                SaleCommittedV1CanonicalParseResult parse =
                    SaleCommittedV1CanonicalJson.Parse(change.PayloadJson);

                if (!parse.IsValid || parse.Payload is null)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        parse.ErrorCode ?? "Sync.ProjectionPayloadInvalid",
                        parse.ErrorMessage ?? "Canonical sale payload could not be parsed.");
                }

                SaleCommittedV1 payload = parse.Payload;
                if (payload.Sale.Id != change.AggregateId)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionAggregateMismatch",
                        "Canonical sale ID does not match change aggregate identity.");
                }

                bool originEcho = change.OriginDeviceId == localDeviceId;
                if (originEcho)
                {
                    LocalSaleFactVerification facts =
                        LocalSaleCommittedV1FactVerifier.VerifyOriginEcho(
                            context.Connection,
                            context.Transaction,
                            change,
                            payload);

                    if (!facts.Matches)
                    {
                        return ReconcileCurrent(
                            context,
                            storeId,
                            expectedSequence,
                            facts.Code ?? "Sync.OriginEchoConflict",
                            facts.Message ?? "Origin echo local durable facts conflict with canonical evidence.");
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

                if (SaleIdOrReceiptExists(
                        context.Connection,
                        context.Transaction,
                        payload.Sale.Id,
                        payload.Sale.ReceiptNumber))
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        "Sync.ProjectionLocalSaleConflict",
                        "Canonical remote sale conflicts with an existing local sale ID or receipt.");
                }

                SaleCommittedV1ProjectionPrerequisiteResult prerequisites =
                    SaleCommittedV1ProjectionPrerequisites.Validate(
                        context.Connection,
                        context.Transaction,
                        payload);

                if (!prerequisites.CanApply)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        prerequisites.Code ?? "Sync.ProjectionPrerequisiteMissing",
                        prerequisites.Message ?? "Projection prerequisite is missing.");
                }

                Result<CompleteCheckoutSaleRequest> mapping = SaleCommittedV1Mapper.ToCore(payload);
                if (mapping.IsFailure)
                {
                    return ReconcileCurrent(
                        context,
                        storeId,
                        expectedSequence,
                        mapping.ErrorDetail.Code,
                        mapping.ErrorDetail.Message);
                }

                _failureInjector.ThrowIfRequested(
                    SyncProjectionFailurePoint.BeforeDomainWrite,
                    expectedSequence);

                Result<SharedSqliteCompletedSaleWriteResult> write =
                    await _saleWriter.ExecuteAsync(
                        context,
                        mapping.Value,
                        token).ConfigureAwait(false);

                if (write.IsFailure)
                {
                    return Result<SyncProjectionResult>.Fail(write.ErrorDetail);
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
            },
            cancellationToken).ConfigureAwait(false);

        if (execution.IsSuccess)
        {
            return execution.Value;
        }

        string code = execution.ErrorDetail.Code;
        string message = execution.ErrorDetail.Message;

        if (message.StartsWith(InjectedFailurePrefix, StringComparison.Ordinal))
        {
            return SyncProjectionResult.Fail(
                attemptedSequence,
                ReadState(storeId),
                "Sync.ProjectionInjectedFailure",
                message);
        }

        if (attemptedSequence.HasValue)
        {
            SyncRemoteState? state = MarkReconciliationAfterRollback(
                storeId,
                attemptedSequence.Value,
                code,
                message);

            return SyncProjectionResult.Reconciliation(
                attemptedSequence,
                state,
                code,
                message);
        }

        return SyncProjectionResult.Fail(
            null,
            ReadState(storeId),
            code,
            message);
    }

    private Result<SyncProjectionResult> ProjectIngredientConsumption(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        IngredientConsumptionCanonicalParseResult parse =
            IngredientConsumptionCommittedV1CanonicalJson.Parse(change.PayloadJson);
        if (!parse.IsValid || parse.Payload is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parse.ErrorCode ?? "IngredientConsumption.ProjectionPayloadInvalid",
                parse.ErrorMessage ?? "Canonical ingredient consumption payload could not be parsed.");
        }

        IngredientConsumptionCommittedV1 payload = parse.Payload;
        if (payload.SaleId != change.AggregateId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "IngredientConsumption.ProjectionAggregateMismatch",
                "Consumed sale ID does not match applied change aggregate identity.");
        }

        bool originEcho = change.OriginDeviceId == localDeviceId;
        if (originEcho)
        {
            Result outbox = VerifyIngredientOriginOutbox(
                context.Connection,
                context.Transaction,
                change,
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

            Result facts = SqliteIngredientConsumptionParticipant.VerifyCanonical(
                context.Connection,
                context.Transaction,
                payload);
            if (facts.IsFailure)
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    facts.ErrorDetail.Code,
                    facts.ErrorDetail.Message);
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

        Result<SaleIngredientConsumptionDecision> applied =
            SqliteIngredientConsumptionParticipant.ApplyCanonical(
                context.Connection,
                context.Transaction,
                payload);
        if (applied.IsFailure)
        {
            // Preserve F1E atomicity: returning failure forces the shared transaction
            // to roll back any partial C2 decision/stock work. ProjectNextAsync then
            // records reconciliation after rollback without advancing the applied cursor.
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

    private static Result VerifyIngredientOriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        IngredientConsumptionCommittedV1 payload)
    {
        Guid expectedOperation =
            IngredientConsumptionCommittedV1Contract.DeriveOperationId(
                payload.SourceSaleOperationId);
        if (change.SourceOperationId != expectedOperation)
        {
            return Result.Fail(Error.Conflict(
                "IngredientConsumption.OriginOperationMismatch",
                "Applied ingredient change does not use the deterministic consumption operation ID."));
        }

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
                "IngredientConsumption.OriginOutboxMissing",
                "Origin echo has no matching durable ingredient-consumption outbox evidence."));
        }

        bool matches =
            Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
            Guid.Parse(reader.GetString(1)) == change.StoreId &&
            reader.GetInt64(2) == change.OriginLocalSequence &&
            string.Equals(
                reader.GetString(3),
                IngredientConsumptionCommittedV1Contract.OperationType,
                StringComparison.Ordinal) &&
            reader.GetInt32(4) == IngredientConsumptionCommittedV1Contract.SchemaVersion &&
            Guid.Parse(reader.GetString(5)) == change.AggregateId &&
            string.Equals(reader.GetString(6), change.PayloadJson, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(7), change.PayloadHash, StringComparison.Ordinal);

        return matches
            ? Result.Success()
            : Result.Fail(Error.Conflict(
                "IngredientConsumption.OriginOutboxMismatch",
                "Origin echo conflicts with retained ingredient-consumption outbox evidence."));
    }

    public async Task<IReadOnlyList<SyncProjectionResult>> ProjectAvailableAsync(
        Guid storeId,
        Guid localDeviceId,
        int maxChanges = 100,
        CancellationToken cancellationToken = default)
    {
        if (maxChanges <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChanges));
        }

        List<SyncProjectionResult> results = new();
        for (int i = 0; i < maxChanges; i++)
        {
            SyncProjectionResult result =
                await ProjectNextAsync(storeId, localDeviceId, cancellationToken)
                    .ConfigureAwait(false);

            results.Add(result);
            if (!result.IsSuccess || !result.HasWork)
            {
                break;
            }
        }

        return results;
    }

    private static Result<SyncProjectionResult> ReconcileCurrent(
        SqliteSharedTransactionContext context,
        Guid storeId,
        long serverSequence,
        string code,
        string message)
    {
        MarkReconciliation(
            context.Connection,
            context.Transaction,
            storeId,
            serverSequence,
            code,
            message);

        SyncRemoteState marked = RequireRemoteState(context, storeId);
        return Result<SyncProjectionResult>.Success(
            SyncProjectionResult.Reconciliation(
                serverSequence,
                marked,
                code,
                message));
    }

    private static void ResolveApplied(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        SyncRemoteState priorState,
        long serverSequence,
        DateTimeOffset appliedAtUtc)
    {
        using (SqliteCommand inbox = connection.CreateCommand())
        {
            inbox.Transaction = transaction;
            inbox.CommandText = """
                UPDATE sync_change_inbox
                SET apply_status=$applied,
                    apply_error=NULL,
                    applied_at_utc=$at
                WHERE store_id=$store
                  AND server_sequence=$sequence
                  AND apply_status=$received;
                """;
            inbox.Parameters.AddWithValue("$applied", SyncProjectionContract.Applied);
            inbox.Parameters.AddWithValue("$at", appliedAtUtc.ToUniversalTime().ToString("O"));
            inbox.Parameters.AddWithValue("$store", storeId.ToString("D"));
            inbox.Parameters.AddWithValue("$sequence", serverSequence);
            inbox.Parameters.AddWithValue("$received", AppliedBusinessChangeContract.Received);

            if (inbox.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Sync.ProjectionInboxResolutionLost: inbox apply transition lost ownership.");
            }
        }

        using SqliteCommand cursor = connection.CreateCommand();
        cursor.Transaction = transaction;
        cursor.CommandText = """
            UPDATE sync_remote_state
            SET last_applied_server_sequence=$applied,
                state=$state,
                last_error=NULL,
                updated_at_utc=$updated
            WHERE store_id=$store
              AND central_instance_id=$instance
              AND last_received_server_sequence >= $applied
              AND last_applied_server_sequence=$expectedPrior;
            """;
        cursor.Parameters.AddWithValue("$applied", serverSequence);
        cursor.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.Ready);
        cursor.Parameters.AddWithValue("$updated", appliedAtUtc.ToUniversalTime().ToString("O"));
        cursor.Parameters.AddWithValue("$store", storeId.ToString("D"));
        cursor.Parameters.AddWithValue("$instance", priorState.CentralInstanceId!.Value.ToString("D"));
        cursor.Parameters.AddWithValue("$expectedPrior", priorState.LastAppliedServerSequence);

        if (cursor.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Sync.ProjectionAppliedCursorLost: applied cursor transition lost ownership.");
        }
    }

    private static void MarkReconciliation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        long? serverSequence,
        string code,
        string message)
    {
        string error = code + ": " + message;

        if (serverSequence.HasValue)
        {
            using SqliteCommand inbox = connection.CreateCommand();
            inbox.Transaction = transaction;
            inbox.CommandText = """
                UPDATE sync_change_inbox
                SET apply_status=$status,
                    apply_error=$error,
                    applied_at_utc=NULL
                WHERE store_id=$store
                  AND server_sequence=$sequence
                  AND apply_status <> $applied;
                """;
            inbox.Parameters.AddWithValue("$status", SyncProjectionContract.ReconciliationRequired);
            inbox.Parameters.AddWithValue("$error", error);
            inbox.Parameters.AddWithValue("$store", storeId.ToString("D"));
            inbox.Parameters.AddWithValue("$sequence", serverSequence.Value);
            inbox.Parameters.AddWithValue("$applied", SyncProjectionContract.Applied);
            inbox.ExecuteNonQuery();
        }

        using SqliteCommand state = connection.CreateCommand();
        state.Transaction = transaction;
        state.CommandText = """
            UPDATE sync_remote_state
            SET state=$state,
                last_error=$error,
                updated_at_utc=$updated
            WHERE store_id=$store;
            """;
        state.Parameters.AddWithValue("$state", AppliedBusinessChangeContract.ReconciliationRequired);
        state.Parameters.AddWithValue("$error", error);
        state.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        state.Parameters.AddWithValue("$store", storeId.ToString("D"));
        state.ExecuteNonQuery();
    }

    private SyncRemoteState? MarkReconciliationAfterRollback(
        Guid storeId,
        long serverSequence,
        string code,
        string message)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        MarkReconciliation(
            connection,
            transaction,
            storeId,
            serverSequence,
            code,
            message);

        SyncRemoteState? state =
            SqliteSyncPullStore.ReadRemoteState(connection, transaction, storeId);
        transaction.Commit();
        return state;
    }

    private SyncRemoteState? ReadState(Guid storeId)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        SyncRemoteState? state =
            SqliteSyncPullStore.ReadRemoteState(connection, transaction, storeId);
        transaction.Commit();
        return state;
    }

    private static SyncRemoteState RequireRemoteState(
        SqliteSharedTransactionContext context,
        Guid storeId) =>
        SqliteSyncPullStore.ReadRemoteState(
            context.Connection,
            context.Transaction,
            storeId)
        ?? throw new InvalidOperationException("Sync remote state disappeared during projection.");

    private static ProjectionInboxRow? ReadInboxRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        long serverSequence)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT server_sequence, change_id, central_instance_id,
                   source_operation_id, origin_device_id, origin_local_sequence,
                   store_id, change_type, schema_version, aggregate_id,
                   payload_json, payload_hash, change_hash,
                   change_created_at_utc, apply_status, apply_error, applied_at_utc
            FROM sync_change_inbox
            WHERE store_id=$store
              AND server_sequence=$sequence
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", serverSequence);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        AppliedBusinessChangeV1 change = new(
            Guid.Parse(reader.GetString(2)),
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            reader.GetInt64(5),
            Guid.Parse(reader.GetString(6)),
            reader.GetString(7),
            reader.GetInt32(8),
            Guid.Parse(reader.GetString(9)),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            DateTimeOffset.Parse(reader.GetString(13)));

        return new ProjectionInboxRow(
            change,
            reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : DateTimeOffset.Parse(reader.GetString(16)));
    }

    private static bool SaleIdOrReceiptExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        string receiptNumber)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM sales
            WHERE id=$id OR receipt_no=$receipt
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        command.Parameters.AddWithValue("$receipt", receiptNumber);
        return command.ExecuteScalar() is not null;
    }

    private sealed record ProjectionInboxRow(
        AppliedBusinessChangeV1 Change,
        string ApplyStatus,
        string? ApplyError,
        DateTimeOffset? AppliedAtUtc);
}
