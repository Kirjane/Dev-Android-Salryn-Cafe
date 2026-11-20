using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private const string CafeBookingProjectionSavepoint =
        "c9_booking_projection";
    private const string CafeBookingV3ProjectionSavepoint =
        "c9_v3_projection";

    private static bool IsCafeBookingAppliedChange(
        AppliedBusinessChangeV1 change) =>
        change.SchemaVersion ==
            AppliedBusinessChangeContract.CafeBookingAppliedSchemaVersion &&
        SqliteCafeBookingSyncParticipant.SourceOperationTypeForApplied(
            change.ChangeType) is not null;

    private static bool IsSaleCommittedV3AppliedChange(
        AppliedBusinessChangeV1 change) =>
        change.SchemaVersion ==
            AppliedBusinessChangeContract.SaleCommittedV3AppliedSchemaVersion &&
        string.Equals(
            change.ChangeType,
            AppliedBusinessChangeContract.SaleCommittedV3AppliedV1,
            StringComparison.Ordinal);

    private Result<SyncProjectionResult> ProjectCafeBooking(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        string? operationType =
            SqliteCafeBookingSyncParticipant
                .SourceOperationTypeForApplied(change.ChangeType);
        if (operationType is null)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeBookingSync.ProjectionContractUnsupported",
                "Applied change is not a supported C9 Booking type.");

        Result<BookingProjectionAuthority> authority =
            ReadBookingProjectionAuthority(
                context.Connection,
                context.Transaction,
                change,
                operationType,
                originEcho: change.OriginDeviceId == localDeviceId);
        if (authority.IsFailure)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                authority.ErrorDetail.Code,
                authority.ErrorDetail.Message);

        BookingProjectionAuthority a = authority.Value;
        if (a.OperationId != change.SourceOperationId ||
            a.BookingId != change.AggregateId ||
            a.StoreId != storeId)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeBookingSync.ProjectionAggregateMismatch",
                "Canonical C9 Booking identity does not match applied change/store authority.");

        SyncOperationEnvelopeV1 envelope = SyncProtocolV1.CreateEnvelope(
            change.OriginDeviceId,
            change.StoreId,
            a.ActorUserId,
            change.OriginLocalSequence,
            operationType,
            CafeBookingOperationContract.SchemaVersion,
            change.AggregateId,
            a.BaseVersion,
            a.OccurredAtUtc,
            change.PayloadJson,
            change.SourceOperationId);

        bool originEcho = change.OriginDeviceId == localDeviceId;
        if (originEcho)
        {
            Result outbox = VerifyCafeBookingOriginOutbox(
                context.Connection,
                context.Transaction,
                change,
                envelope,
                a);
            if (outbox.IsFailure)
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    outbox.ErrorDetail.Code,
                    outbox.ErrorDetail.Message);

            if (!SqliteCafeBookingSyncParticipant.VerifyCanonical(
                    context.Connection,
                    context.Transaction,
                    envelope))
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    "CafeBookingSync.OriginFactMismatch",
                    "Origin echo conflicts with retained C9 Booking durable facts.");

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
                    true,
                    resolved,
                    SyncProjectionContract.OriginEchoResolvedCode));
        }

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.BeforeDomainWrite,
            expectedSequence);

        ExecC9(
            context.Connection,
            context.Transaction,
            "SAVEPOINT " + CafeBookingProjectionSavepoint + ";");

        Result applied = SqliteCafeBookingSyncParticipant.ApplyCanonical(
            context.Connection,
            context.Transaction,
            envelope);

        if (applied.IsFailure)
        {
            ExecC9(
                context.Connection,
                context.Transaction,
                "ROLLBACK TO " + CafeBookingProjectionSavepoint + ";");
            ExecC9(
                context.Connection,
                context.Transaction,
                "RELEASE " + CafeBookingProjectionSavepoint + ";");

            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                applied.ErrorDetail.Code,
                applied.ErrorDetail.Message);
        }

        ExecC9(
            context.Connection,
            context.Transaction,
            "RELEASE " + CafeBookingProjectionSavepoint + ";");

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
                false,
                finalState,
                SyncProjectionContract.ProjectionAppliedCode));
    }

    private async Task<Result<SyncProjectionResult>>
        ProjectSaleCommittedV3Async(
            SqliteSharedTransactionContext context,
            Guid storeId,
            Guid localDeviceId,
            SyncRemoteState state,
            long expectedSequence,
            AppliedBusinessChangeV1 change,
            CancellationToken cancellationToken)
    {
        SaleCommittedV3ParseResult parse =
            SaleCommittedV3CanonicalJson.Parse(change.PayloadJson);
        if (!parse.IsValid || parse.Payload is null)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parse.ErrorCode ?? "SaleCommittedV3.ProjectionPayloadInvalid",
                parse.ErrorMessage ??
                    "Canonical SaleCommittedV3 payload could not be parsed.");

        SaleCommittedV3 payload = parse.Payload;
        if (payload.SaleEvidence.Sale.Id != change.AggregateId)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "SaleCommittedV3.ProjectionAggregateMismatch",
                "V3 SaleId does not match applied change aggregate.");

        SyncOperationEnvelopeV1 envelope = SyncProtocolV1.CreateEnvelope(
            change.OriginDeviceId,
            change.StoreId,
            payload.SaleEvidence.Sale.CashierUserId,
            change.OriginLocalSequence,
            SaleCommittedV3Contract.OperationType,
            SaleCommittedV3Contract.SchemaVersion,
            change.AggregateId,
            null,
            payload.FinancialDecisionCreatedAtUtc,
            change.PayloadJson,
            change.SourceOperationId);

        bool originEcho = change.OriginDeviceId == localDeviceId;
        if (originEcho)
        {
            Result outbox = VerifyV3OriginOutbox(
                context.Connection,
                context.Transaction,
                change,
                envelope);
            if (outbox.IsFailure)
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    outbox.ErrorDetail.Code,
                    outbox.ErrorDetail.Message);

            LocalSaleFactVerification facts =
                SharedSqliteCafeBookingV3Participant.VerifyOriginEcho(
                    context.Connection,
                    context.Transaction,
                    change,
                    payload);
            if (!facts.Matches)
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    facts.Code ?? "SaleCommittedV3.OriginFactMismatch",
                    facts.Message ??
                        "Origin V3 echo conflicts with retained local facts.");

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
                    true,
                    resolved,
                    SyncProjectionContract.OriginEchoResolvedCode));
        }

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.BeforeDomainWrite,
            expectedSequence);

        ExecC9(
            context.Connection,
            context.Transaction,
            "SAVEPOINT " + CafeBookingV3ProjectionSavepoint + ";");

        Result apply =
            await SharedSqliteCafeBookingV3Participant.ApplyCanonicalAsync(
                context,
                envelope,
                payload,
                cancellationToken).ConfigureAwait(false);

        if (apply.IsFailure)
        {
            ExecC9(
                context.Connection,
                context.Transaction,
                "ROLLBACK TO " + CafeBookingV3ProjectionSavepoint + ";");
            ExecC9(
                context.Connection,
                context.Transaction,
                "RELEASE " + CafeBookingV3ProjectionSavepoint + ";");

            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                apply.ErrorDetail.Code,
                apply.ErrorDetail.Message);
        }

        ExecC9(
            context.Connection,
            context.Transaction,
            "RELEASE " + CafeBookingV3ProjectionSavepoint + ";");

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
                false,
                finalState,
                SyncProjectionContract.ProjectionAppliedCode));
    }

    private static Result<BookingProjectionAuthority>
        ReadBookingProjectionAuthority(
            SqliteConnection connection,
            SqliteTransaction transaction,
            AppliedBusinessChangeV1 change,
            string operationType,
            bool originEcho)
    {
        if (operationType ==
            CafeBookingOperationContract.BookingRevisionCommittedV1)
        {
            var p = CafeBookingCanonicalJson.ParseRevision(change.PayloadJson);
            if (!p.IsValid || p.Payload is null)
                return FailAuthority(p.ErrorCode, p.ErrorMessage);
            return Result<BookingProjectionAuthority>.Success(
                new(
                    p.Payload.OperationId,
                    p.Payload.BookingId,
                    p.Payload.StoreId,
                    p.Payload.ActorUserId,
                    p.Payload.ExpectedVersion,
                    p.Payload.OccurredAtUtc));
        }

        if (operationType ==
            CafeBookingOperationContract.BookingStatusAdvancedV1)
        {
            var p = CafeBookingCanonicalJson.ParseStatus(change.PayloadJson);
            if (!p.IsValid || p.Payload is null)
                return FailAuthority(p.ErrorCode, p.ErrorMessage);
            return Result<BookingProjectionAuthority>.Success(
                new(
                    p.Payload.OperationId,
                    p.Payload.BookingId,
                    p.Payload.StoreId,
                    p.Payload.ActorUserId,
                    p.Payload.ExpectedVersion,
                    p.Payload.OccurredAtUtc));
        }

        if (operationType ==
            CafeBookingOperationContract.BookingCancelledV1)
        {
            var p = CafeBookingCanonicalJson.ParseCancellation(change.PayloadJson);
            if (!p.IsValid || p.Payload is null)
                return FailAuthority(p.ErrorCode, p.ErrorMessage);
            return Result<BookingProjectionAuthority>.Success(
                new(
                    p.Payload.OperationId,
                    p.Payload.BookingId,
                    p.Payload.StoreId,
                    p.Payload.ActorUserId,
                    p.Payload.ExpectedVersion,
                    p.Payload.OccurredAtUtc));
        }

        if (operationType ==
            BookingFulfillmentOrderBoundV1Contract.OperationType)
        {
            var p = BookingFulfillmentOrderBoundV1CanonicalJson.Parse(
                change.PayloadJson);
            if (!p.IsValid || p.Payload is null)
                return FailAuthority(p.ErrorCode, p.ErrorMessage);
            return Result<BookingProjectionAuthority>.Success(
                new(
                    p.Payload.OperationId,
                    p.Payload.BookingId,
                    p.Payload.StoreId,
                    p.Payload.ActorUserId,
                    p.Payload.ExpectedBookingVersion,
                    p.Payload.OccurredAtUtc));
        }

        Guid operationId;
        Guid bookingId;
        Guid sourceStore;
        Guid actor;
        DateTimeOffset at;

        if (operationType ==
            CafeBookingOperationContract.BookingDepositReceivedV1)
        {
            var p = CafeBookingCanonicalJson.ParseDepositReceive(
                change.PayloadJson);
            if (!p.IsValid || p.Payload is null)
                return FailAuthority(p.ErrorCode, p.ErrorMessage);
            operationId = p.Payload.OperationId;
            bookingId = p.Payload.BookingId;
            sourceStore = p.Payload.StoreId;
            actor = p.Payload.ActorUserId;
            at = p.Payload.OccurredAtUtc;
        }
        else
        {
            var p = CafeBookingCanonicalJson.ParseDepositRefund(
                change.PayloadJson);
            if (!p.IsValid || p.Payload is null)
                return FailAuthority(p.ErrorCode, p.ErrorMessage);
            operationId = p.Payload.OperationId;
            bookingId = p.Payload.BookingId;
            sourceStore = p.Payload.StoreId;
            actor = p.Payload.ActorUserId;
            at = p.Payload.OccurredAtUtc;
        }

        long? baseVersion = originEcho
            ? ReadOperationExpectedVersion(
                connection,
                transaction,
                operationId)
            : ReadBookingCurrentVersion(
                connection,
                transaction,
                bookingId);

        if (!baseVersion.HasValue)
            return FailAuthority(
                "CafeBookingSync.ProjectionVersionPrerequisiteMissing",
                "C9 deposit projection cannot resolve retained Booking version authority.");

        return Result<BookingProjectionAuthority>.Success(
            new(
                operationId,
                bookingId,
                sourceStore,
                actor,
                baseVersion.Value,
                at));
    }

    private static Result VerifyCafeBookingOriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SyncOperationEnvelopeV1 envelope,
        BookingProjectionAuthority authority)
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
            return Result.Fail(
                Error.Conflict(
                    "CafeBookingSync.OriginOutboxMissing",
                    "Origin echo has no matching durable C9 Booking outbox."));

        bool matches =
            Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
            Guid.Parse(reader.GetString(1)) == change.StoreId &&
            Guid.Parse(reader.GetString(2)) == authority.ActorUserId &&
            reader.GetInt64(3) == change.OriginLocalSequence &&
            string.Equals(
                reader.GetString(4),
                envelope.OperationType,
                StringComparison.Ordinal) &&
            reader.GetInt32(5) ==
                CafeBookingOperationContract.SchemaVersion &&
            Guid.Parse(reader.GetString(6)) == change.AggregateId &&
            !reader.IsDBNull(7) &&
            reader.GetInt64(7) == authority.BaseVersion &&
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
                    "CafeBookingSync.OriginOutboxMismatch",
                    "Origin echo conflicts with retained C9 Booking outbox evidence."));
    }

    private static Result VerifyV3OriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SyncOperationEnvelopeV1 envelope)
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
            return Result.Fail(
                Error.Conflict(
                    "SaleCommittedV3.OriginOutboxMissing",
                    "Origin V3 echo has no matching durable source outbox."));

        bool matches =
            Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
            Guid.Parse(reader.GetString(1)) == change.StoreId &&
            Guid.Parse(reader.GetString(2)) == envelope.ActorUserId &&
            reader.GetInt64(3) == change.OriginLocalSequence &&
            string.Equals(
                reader.GetString(4),
                SaleCommittedV3Contract.OperationType,
                StringComparison.Ordinal) &&
            reader.GetInt32(5) ==
                SaleCommittedV3Contract.SchemaVersion &&
            Guid.Parse(reader.GetString(6)) == change.AggregateId &&
            reader.IsDBNull(7) &&
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
                    "SaleCommittedV3.OriginOutboxMismatch",
                    "Origin V3 echo conflicts with retained source outbox."));
    }

    private static long? ReadOperationExpectedVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT expected_version
            FROM cafe_booking_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            operationId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value);
    }

    private static long? ReadBookingCurrentVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT current_version
            FROM cafe_bookings
            WHERE booking_id=$booking
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$booking",
            bookingId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value);
    }

    private static Result<BookingProjectionAuthority> FailAuthority(
        string? code,
        string? message) =>
        Result<BookingProjectionAuthority>.Fail(
            Error.Conflict(
                code ?? "CafeBookingSync.ProjectionPayloadInvalid",
                message ?? "C9 Booking projection authority is invalid."));

    private static void ExecC9(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record BookingProjectionAuthority(
        Guid OperationId,
        Guid BookingId,
        Guid StoreId,
        Guid ActorUserId,
        long BaseVersion,
        DateTimeOffset OccurredAtUtc);
}
