using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private const string CafeAttendanceIncidentProjectionSavepoint =
        "c10_attendance_incident_projection";

    private static bool IsCafeAttendanceIncidentAppliedChange(
        AppliedBusinessChangeV1 change) =>
        change.SchemaVersion ==
            AppliedBusinessChangeContract.CafeAttendanceIncidentAppliedSchemaVersion &&
        SqliteCafeAttendanceIncidentSyncParticipant
            .SourceOperationTypeForApplied(change.ChangeType) is not null;

    private Result<SyncProjectionResult> ProjectCafeAttendanceIncident(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        string? operationType =
            SqliteCafeAttendanceIncidentSyncParticipant
                .SourceOperationTypeForApplied(change.ChangeType);

        if (operationType is null)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeAttendanceIncidentSync.ProjectionContractUnsupported",
                "Applied change is not a supported C10 type.");

        Result<C10ProjectionAuthority> authority =
            ReadC10ProjectionAuthority(
                change,
                operationType);

        if (authority.IsFailure)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                authority.ErrorDetail.Code,
                authority.ErrorDetail.Message);

        C10ProjectionAuthority a = authority.Value;

        if (a.OperationId != change.SourceOperationId ||
            a.AggregateId != change.AggregateId ||
            a.StoreId != storeId)
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeAttendanceIncidentSync.ProjectionAggregateMismatch",
                "Canonical C10 identity does not match applied change/store authority.");

        SyncOperationEnvelopeV1 envelope =
            SyncProtocolV1.CreateEnvelope(
                change.OriginDeviceId,
                change.StoreId,
                a.ActorUserId,
                change.OriginLocalSequence,
                operationType,
                CafeAttendanceIncidentSyncContract.SchemaVersion,
                change.AggregateId,
                a.BaseVersion,
                a.OccurredAtUtc,
                change.PayloadJson,
                change.SourceOperationId);

        bool originEcho =
            change.OriginDeviceId == localDeviceId;

        if (originEcho)
        {
            if (!VerifyC10OriginOutbox(
                    context.Connection,
                    context.Transaction,
                    change,
                    envelope) ||
                !SqliteCafeAttendanceIncidentSyncParticipant.VerifyCanonical(
                    context.Connection,
                    context.Transaction,
                    envelope))
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    "CafeAttendanceIncidentSync.OriginFactMismatch",
                    "Origin echo conflicts with retained C10 outbox/domain evidence.");
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
                    true,
                    resolved,
                    SyncProjectionContract.OriginEchoResolvedCode));
        }

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.BeforeDomainWrite,
            expectedSequence);

        ExecC10(
            context.Connection,
            context.Transaction,
            "SAVEPOINT " +
            CafeAttendanceIncidentProjectionSavepoint +
            ";");

        Result applied =
            SqliteCafeAttendanceIncidentSyncParticipant.ApplyCanonical(
                context.Connection,
                context.Transaction,
                envelope);

        if (applied.IsFailure)
        {
            ExecC10(
                context.Connection,
                context.Transaction,
                "ROLLBACK TO " +
                CafeAttendanceIncidentProjectionSavepoint +
                ";");
            ExecC10(
                context.Connection,
                context.Transaction,
                "RELEASE " +
                CafeAttendanceIncidentProjectionSavepoint +
                ";");

            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                applied.ErrorDetail.Code,
                applied.ErrorDetail.Message);
        }

        ExecC10(
            context.Connection,
            context.Transaction,
            "RELEASE " +
            CafeAttendanceIncidentProjectionSavepoint +
            ";");

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

    private static Result<C10ProjectionAuthority>
        ReadC10ProjectionAuthority(
            AppliedBusinessChangeV1 change,
            string operationType)
    {
        if (operationType ==
            CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1)
        {
            var parsed =
                CafeAttendanceIncidentCanonicalJson
                    .Parse<AttendanceEventRecordedV1>(
                        change.PayloadJson);

            if (!parsed.IsValid || parsed.Payload is null)
                return FailC10Authority(
                    parsed.ErrorCode,
                    parsed.ErrorMessage);

            var p = parsed.Payload.Event;

            return Result<C10ProjectionAuthority>.Success(
                new(
                    p.OperationId,
                    p.AttendanceEventId,
                    p.StoreId,
                    p.ActorUserId,
                    null,
                    p.OccurredAtUtc));
        }

        if (operationType ==
            CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1)
        {
            var parsed =
                CafeAttendanceIncidentCanonicalJson
                    .Parse<AttendanceEventCorrectedV1>(
                        change.PayloadJson);

            if (!parsed.IsValid || parsed.Payload is null)
                return FailC10Authority(
                    parsed.ErrorCode,
                    parsed.ErrorMessage);

            var p = parsed.Payload;

            return Result<C10ProjectionAuthority>.Success(
                new(
                    p.OperationId,
                    p.AttendanceEventId,
                    p.StoreId,
                    p.ActorUserId,
                    null,
                    p.OccurredAtUtc));
        }

        if (operationType ==
            CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1)
        {
            var parsed =
                CafeAttendanceIncidentCanonicalJson
                    .Parse<IncidentRevisionCommittedV1>(
                        change.PayloadJson);

            if (!parsed.IsValid || parsed.Payload is null)
                return FailC10Authority(
                    parsed.ErrorCode,
                    parsed.ErrorMessage);

            var p = parsed.Payload;

            return Result<C10ProjectionAuthority>.Success(
                new(
                    p.OperationId,
                    p.IncidentId,
                    p.StoreId,
                    p.ActorUserId,
                    p.ExpectedVersion,
                    p.OccurredAtUtc));
        }

        if (operationType ==
            CafeAttendanceIncidentSyncContract.IncidentResolvedV1)
        {
            var parsed =
                CafeAttendanceIncidentCanonicalJson
                    .Parse<IncidentResolvedV1>(
                        change.PayloadJson);

            if (!parsed.IsValid || parsed.Payload is null)
                return FailC10Authority(
                    parsed.ErrorCode,
                    parsed.ErrorMessage);

            var p = parsed.Payload;

            return Result<C10ProjectionAuthority>.Success(
                new(
                    p.OperationId,
                    p.IncidentId,
                    p.StoreId,
                    p.ActorUserId,
                    p.ExpectedVersion,
                    p.ResolvedAtUtc));
        }

        return Result<C10ProjectionAuthority>.Fail(
            Error.Conflict(
                "CafeAttendanceIncidentSync.ProjectionContractUnsupported",
                "Unsupported C10 applied-change contract."));
    }

    private static Result<C10ProjectionAuthority>
        FailC10Authority(
            string? code,
            string? message) =>
        Result<C10ProjectionAuthority>.Fail(
            Error.Conflict(
                code ??
                    "CafeAttendanceIncidentSync.ProjectionPayloadInvalid",
                message ??
                    "C10 applied-change payload is invalid."));

    private static bool VerifyC10OriginOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SyncOperationEnvelopeV1 envelope)
    {
        using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT device_id,store_id,actor_user_id,local_sequence,
                   operation_type,schema_version,aggregate_id,
                   base_version,payload_json,payload_hash
            FROM sync_outbox
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            change.SourceOperationId.ToString("D"));

        using SqliteDataReader reader =
            command.ExecuteReader();

        if (!reader.Read())
            return false;

        long? baseVersion =
            reader.IsDBNull(7)
                ? null
                : reader.GetInt64(7);

        return
            Guid.Parse(reader.GetString(0)) ==
                envelope.DeviceId &&
            Guid.Parse(reader.GetString(1)) ==
                envelope.StoreId &&
            Guid.Parse(reader.GetString(2)) ==
                envelope.ActorUserId &&
            reader.GetInt64(3) ==
                envelope.LocalSequence &&
            string.Equals(
                reader.GetString(4),
                envelope.OperationType,
                StringComparison.Ordinal) &&
            reader.GetInt32(5) ==
                envelope.SchemaVersion &&
            Guid.Parse(reader.GetString(6)) ==
                envelope.AggregateId &&
            baseVersion ==
                envelope.BaseVersion &&
            string.Equals(
                reader.GetString(8),
                envelope.PayloadJson,
                StringComparison.Ordinal) &&
            string.Equals(
                reader.GetString(9),
                envelope.PayloadHash,
                StringComparison.Ordinal);
    }

    private static void ExecC10(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record C10ProjectionAuthority(
        Guid OperationId,
        Guid AggregateId,
        Guid StoreId,
        Guid ActorUserId,
        long? BaseVersion,
        DateTimeOffset OccurredAtUtc);
}
