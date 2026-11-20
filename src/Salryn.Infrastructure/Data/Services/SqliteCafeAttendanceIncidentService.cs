using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeAttendanceIncidentService :
    SqliteRepositoryBase,
    ICafeAttendanceIncidentService
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeAttendanceIncidentService(
        SqliteConnectionFactory connectionFactory)
        : base(
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory)))
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
    }

    public Result<CafeAttendanceMutationResult> RecordAttendance(
        CafeAttendanceRecordMutation mutation)
    {
        string? validation = ValidateAttendanceMutation(mutation);
        if (validation is not null)
            return Result<CafeAttendanceMutationResult>.Fail(
                Error.Validation("CafeAttendance.RecordInvalid", validation));

        DateTimeOffset occurred = mutation.OccurredAtUtc.ToUniversalTime();
        DateTimeOffset recorded = DateTimeOffset.UtcNow;
        string payloadHash = Hash(
            "AttendanceEventRecordedV1",
            mutation.OperationId,
            mutation.AttendanceEventId,
            mutation.StoreId,
            mutation.SubjectUserId,
            mutation.ActorUserId,
            mutation.DeviceId,
            mutation.EventType,
            occurred.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.AttendanceRecord);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeAttendanceMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            if (mutation.SubjectUserId != mutation.ActorUserId &&
                !HasPermission(
                    connection,
                    transaction,
                    mutation.ActorUserId,
                    SalrynPermissions.AttendanceManage))
            {
                transaction.Rollback();
                return Result<CafeAttendanceMutationResult>.Fail(
                    Error.Forbidden(
                        "CafeAttendance.ManageRequiredForOtherUser",
                        "Recording attendance for another user requires Attendance.Manage."));
            }

            if (!ActiveUserExists(
                    connection,
                    transaction,
                    mutation.SubjectUserId))
            {
                transaction.Rollback();
                return Result<CafeAttendanceMutationResult>.Fail(
                    Error.Validation(
                        "CafeAttendance.SubjectMissing",
                        "Attendance subject must be an active existing user."));
            }

            Result<CafeAttendanceMutationResult>? replay =
                TryReplayAttendance(
                    connection,
                    transaction,
                    mutation,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            EnsureOperationUnused(
                connection,
                transaction,
                mutation.OperationId);

            InsertAttendanceEvent(
                connection,
                transaction,
                mutation.AttendanceEventId,
                mutation.OperationId,
                mutation.StoreId,
                mutation.SubjectUserId,
                mutation.ActorUserId,
                mutation.DeviceId,
                mutation.EventType,
                occurred,
                recorded,
                payloadHash);

            CafeAttendanceEventEvidenceV1 evidence = new(
                mutation.AttendanceEventId,
                mutation.OperationId,
                mutation.StoreId,
                mutation.SubjectUserId,
                mutation.ActorUserId,
                mutation.DeviceId,
                mutation.EventType,
                occurred,
                recorded,
                payloadHash);

            string json =
                CafeAttendanceIncidentCanonicalJson.Serialize(
                    new AttendanceEventRecordedV1(evidence));

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1,
                    CafeAttendanceIncidentSyncContract.SchemaVersion,
                    mutation.AttendanceEventId,
                    null,
                    occurred,
                    json,
                    mutation.OperationId));

            transaction.Commit();

            return Result<CafeAttendanceMutationResult>.Success(
                new(
                    mutation.AttendanceEventId,
                    mutation.OperationId,
                    mutation.SubjectUserId,
                    mutation.EventType,
                    false));
        }
        catch (C10ValidationException ex)
        {
            return Result<CafeAttendanceMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeAttendanceMutationResult>.Fail(
                Error.Conflict(
                    "CafeAttendance.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeAttendanceMutationResult>.Fail(
                Error.Unexpected(
                    "CafeAttendance.RecordFailed",
                    ex.Message));
        }
    }

    public Result<CafeAttendanceCorrectionResult> CorrectAttendance(
        CafeAttendanceCorrectMutation mutation)
    {
        string? validation = ValidateCorrectionMutation(mutation);
        if (validation is not null)
            return Result<CafeAttendanceCorrectionResult>.Fail(
                Error.Validation("CafeAttendance.CorrectionInvalid", validation));

        string reason = mutation.Reason.Trim();
        DateTimeOffset occurred = mutation.OccurredAtUtc.ToUniversalTime();

        string payloadHash = Hash(
            "AttendanceEventCorrectedV1",
            mutation.OperationId,
            mutation.AttendanceCorrectionId,
            mutation.StoreId,
            mutation.AttendanceEventId,
            mutation.ActorUserId,
            mutation.DeviceId,
            mutation.CorrectionKind,
            reason,
            occurred.ToString("O", CultureInfo.InvariantCulture),
            mutation.ReplacementAttendanceEventId,
            mutation.ReplacementEventType,
            mutation.ReplacementOccurredAtUtc?.ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.AttendanceManage);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeAttendanceCorrectionResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafeAttendanceCorrectionResult>? replay =
                TryReplayCorrection(
                    connection,
                    transaction,
                    mutation,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            EnsureOperationUnused(
                connection,
                transaction,
                mutation.OperationId);

            AttendanceBaseRow original =
                ReadAttendanceBase(
                    connection,
                    transaction,
                    mutation.AttendanceEventId)
                ?? throw new C10ValidationException(
                    "CafeAttendance.EventMissing",
                    "Attendance event was not found.");

            if (original.StoreId != mutation.StoreId)
                throw new C10ValidationException(
                    "CafeAttendance.StoreMismatch",
                    "Attendance correction must remain in the original store.");

            if (AttendanceAlreadyCorrected(
                    connection,
                    transaction,
                    mutation.AttendanceEventId))
                throw new C10ValidationException(
                    "CafeAttendance.AlreadyCorrected",
                    "Attendance event already has a direct correction.");

            CafeAttendanceEventEvidenceV1? replacement = null;

            if (string.Equals(
                    mutation.CorrectionKind,
                    CafeAttendanceCorrectionKinds.Replace,
                    StringComparison.Ordinal))
            {
                Guid replacementId =
                    mutation.ReplacementAttendanceEventId!.Value;
                string replacementType =
                    mutation.ReplacementEventType!;
                DateTimeOffset replacementOccurred =
                    mutation.ReplacementOccurredAtUtc!.Value.ToUniversalTime();

                string replacementHash = Hash(
                    "AttendanceReplacementV1",
                    mutation.OperationId,
                    replacementId,
                    mutation.StoreId,
                    original.SubjectUserId,
                    mutation.ActorUserId,
                    mutation.DeviceId,
                    replacementType,
                    replacementOccurred.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                InsertAttendanceEvent(
                    connection,
                    transaction,
                    replacementId,
                    mutation.OperationId,
                    mutation.StoreId,
                    original.SubjectUserId,
                    mutation.ActorUserId,
                    mutation.DeviceId,
                    replacementType,
                    replacementOccurred,
                    occurred,
                    replacementHash);

                replacement = new(
                    replacementId,
                    mutation.OperationId,
                    mutation.StoreId,
                    original.SubjectUserId,
                    mutation.ActorUserId,
                    mutation.DeviceId,
                    replacementType,
                    replacementOccurred,
                    occurred,
                    replacementHash);
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_attendance_corrections(
                        attendance_correction_id,operation_id,store_id,
                        attendance_event_id,actor_user_id,device_id,
                        correction_kind,replacement_attendance_event_id,
                        reason,occurred_at_utc,payload_hash)
                    VALUES(
                        $correction,$operation,$store,$event,$actor,$device,
                        $kind,$replacement,$reason,$at,$hash);
                    """;
                command.Parameters.AddWithValue(
                    "$correction",
                    mutation.AttendanceCorrectionId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$operation",
                    mutation.OperationId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$store",
                    mutation.StoreId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$event",
                    mutation.AttendanceEventId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$actor",
                    mutation.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$device",
                    mutation.DeviceId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$kind",
                    mutation.CorrectionKind);
                command.Parameters.AddWithValue(
                    "$replacement",
                    mutation.ReplacementAttendanceEventId.HasValue
                        ? mutation.ReplacementAttendanceEventId.Value.ToString("D")
                        : DBNull.Value);
                command.Parameters.AddWithValue("$reason", reason);
                command.Parameters.AddWithValue(
                    "$at",
                    occurred.ToString("O"));
                command.Parameters.AddWithValue(
                    "$hash",
                    payloadHash);
                command.ExecuteNonQuery();
            }

            AttendanceEventCorrectedV1 sync = new(
                mutation.OperationId,
                mutation.AttendanceCorrectionId,
                mutation.StoreId,
                mutation.AttendanceEventId,
                mutation.ActorUserId,
                mutation.DeviceId,
                mutation.CorrectionKind,
                reason,
                occurred,
                mutation.ReplacementAttendanceEventId,
                replacement,
                payloadHash);

            string json =
                CafeAttendanceIncidentCanonicalJson.Serialize(sync);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1,
                    CafeAttendanceIncidentSyncContract.SchemaVersion,
                    mutation.AttendanceEventId,
                    null,
                    occurred,
                    json,
                    mutation.OperationId));

            transaction.Commit();

            return Result<CafeAttendanceCorrectionResult>.Success(
                new(
                    mutation.AttendanceCorrectionId,
                    mutation.AttendanceEventId,
                    mutation.CorrectionKind,
                    mutation.ReplacementAttendanceEventId,
                    false));
        }
        catch (C10ValidationException ex)
        {
            return Result<CafeAttendanceCorrectionResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeAttendanceCorrectionResult>.Fail(
                Error.Conflict(
                    "CafeAttendance.CorrectionConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeAttendanceCorrectionResult>.Fail(
                Error.Unexpected(
                    "CafeAttendance.CorrectionFailed",
                    ex.Message));
        }
    }

    public Result<IReadOnlyList<CafeAttendanceEvent>> ListAttendance(
        Guid storeId,
        Guid actorUserId,
        Guid? subjectUserId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null)
    {
        if (storeId == Guid.Empty || actorUserId == Guid.Empty)
            return Result<IReadOnlyList<CafeAttendanceEvent>>.Fail(
                Error.Validation(
                    "CafeAttendance.ListIdentityInvalid",
                    "Store and actor identity are required."));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.AttendanceView);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<IReadOnlyList<CafeAttendanceEvent>>.Fail(
                    authority.ErrorDetail);
            }

            bool manage = HasPermission(
                connection,
                transaction,
                actorUserId,
                SalrynPermissions.AttendanceManage);

            Guid effectiveSubject =
                subjectUserId ?? actorUserId;

            if (!manage && effectiveSubject != actorUserId)
            {
                transaction.Rollback();
                return Result<IReadOnlyList<CafeAttendanceEvent>>.Fail(
                    Error.Forbidden(
                        "CafeAttendance.OtherUserVisibilityForbidden",
                        "Attendance.View without Attendance.Manage is limited to the signed-in user's history."));
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    e.attendance_event_id,e.operation_id,e.store_id,
                    e.subject_user_id,e.actor_user_id,e.device_id,
                    e.event_type,e.occurred_at_utc,e.recorded_at_utc,
                    e.payload_hash,
                    c.attendance_correction_id,c.correction_kind,
                    c.replacement_attendance_event_id,c.reason
                FROM cafe_attendance_events e
                LEFT JOIN cafe_attendance_corrections c
                  ON c.attendance_event_id=e.attendance_event_id
                WHERE e.store_id=$store
                  AND e.subject_user_id=$subject
                  AND ($from IS NULL OR e.occurred_at_utc >= $from)
                  AND ($to IS NULL OR e.occurred_at_utc <= $to)
                ORDER BY e.occurred_at_utc DESC,e.attendance_event_id ASC;
                """;
            command.Parameters.AddWithValue(
                "$store",
                storeId.ToString("D"));
            command.Parameters.AddWithValue(
                "$subject",
                effectiveSubject.ToString("D"));
            command.Parameters.AddWithValue(
                "$from",
                fromUtc.HasValue
                    ? fromUtc.Value.ToUniversalTime().ToString("O")
                    : DBNull.Value);
            command.Parameters.AddWithValue(
                "$to",
                toUtc.HasValue
                    ? toUtc.Value.ToUniversalTime().ToString("O")
                    : DBNull.Value);

            List<CafeAttendanceEvent> result = new();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Guid? correctionId =
                    reader.IsDBNull(10)
                        ? null
                        : Guid.Parse(reader.GetString(10));

                result.Add(new(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    Guid.Parse(reader.GetString(3)),
                    Guid.Parse(reader.GetString(4)),
                    Guid.Parse(reader.GetString(5)),
                    reader.GetString(6),
                    ParseDate(reader.GetString(7)),
                    ParseDate(reader.GetString(8)),
                    reader.GetString(9),
                    correctionId is null,
                    correctionId,
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12)),
                    reader.IsDBNull(13) ? null : reader.GetString(13)));
            }

            transaction.Commit();
            return Result<IReadOnlyList<CafeAttendanceEvent>>.Success(result);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<IReadOnlyList<CafeAttendanceEvent>>.Fail(
                Error.Unexpected(
                    "CafeAttendance.ListFailed",
                    ex.Message));
        }
    }

    private static Result<CafeAttendanceMutationResult>? TryReplayAttendance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeAttendanceRecordMutation mutation,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attendance_event_id,store_id,subject_user_id,actor_user_id,
                   device_id,event_type,payload_hash
            FROM cafe_attendance_events
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            mutation.OperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        bool same =
            Guid.Parse(reader.GetString(0)) == mutation.AttendanceEventId &&
            Guid.Parse(reader.GetString(1)) == mutation.StoreId &&
            Guid.Parse(reader.GetString(2)) == mutation.SubjectUserId &&
            Guid.Parse(reader.GetString(3)) == mutation.ActorUserId &&
            Guid.Parse(reader.GetString(4)) == mutation.DeviceId &&
            string.Equals(reader.GetString(5), mutation.EventType, StringComparison.Ordinal) &&
            string.Equals(reader.GetString(6), payloadHash, StringComparison.Ordinal);

        return same
            ? Result<CafeAttendanceMutationResult>.Success(
                new(
                    mutation.AttendanceEventId,
                    mutation.OperationId,
                    mutation.SubjectUserId,
                    mutation.EventType,
                    true))
            : Result<CafeAttendanceMutationResult>.Fail(
                Error.Conflict(
                    "CafeAttendance.OperationCollision",
                    "OperationId already exists with different attendance evidence."));
    }

    private static Result<CafeAttendanceCorrectionResult>? TryReplayCorrection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeAttendanceCorrectMutation mutation,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attendance_correction_id,attendance_event_id,
                   correction_kind,replacement_attendance_event_id,payload_hash
            FROM cafe_attendance_corrections
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            mutation.OperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        Guid? replacement =
            reader.IsDBNull(3)
                ? null
                : Guid.Parse(reader.GetString(3));

        bool same =
            Guid.Parse(reader.GetString(0)) == mutation.AttendanceCorrectionId &&
            Guid.Parse(reader.GetString(1)) == mutation.AttendanceEventId &&
            string.Equals(reader.GetString(2), mutation.CorrectionKind, StringComparison.Ordinal) &&
            replacement == mutation.ReplacementAttendanceEventId &&
            string.Equals(reader.GetString(4), payloadHash, StringComparison.Ordinal);

        return same
            ? Result<CafeAttendanceCorrectionResult>.Success(
                new(
                    mutation.AttendanceCorrectionId,
                    mutation.AttendanceEventId,
                    mutation.CorrectionKind,
                    mutation.ReplacementAttendanceEventId,
                    true))
            : Result<CafeAttendanceCorrectionResult>.Fail(
                Error.Conflict(
                    "CafeAttendance.CorrectionOperationCollision",
                    "OperationId already exists with different correction evidence."));
    }

    private static void InsertAttendanceEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid eventId,
        Guid operationId,
        Guid storeId,
        Guid subjectUserId,
        Guid actorUserId,
        Guid deviceId,
        string eventType,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_attendance_events(
                attendance_event_id,operation_id,store_id,
                subject_user_id,actor_user_id,device_id,event_type,
                occurred_at_utc,recorded_at_utc,payload_hash)
            VALUES(
                $event,$operation,$store,$subject,$actor,$device,$type,
                $occurred,$recorded,$hash);
            """;
        command.Parameters.AddWithValue("$event", eventId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$subject", subjectUserId.ToString("D"));
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue(
            "$occurred",
            occurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$recorded",
            recordedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.ExecuteNonQuery();
    }

    private static AttendanceBaseRow? ReadAttendanceBase(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid attendanceEventId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id,subject_user_id
            FROM cafe_attendance_events
            WHERE attendance_event_id=$event
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$event",
            attendanceEventId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)))
            : null;
    }

    private static bool AttendanceAlreadyCorrected(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid attendanceEventId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cafe_attendance_corrections
            WHERE attendance_event_id=$event;
            """;
        command.Parameters.AddWithValue(
            "$event",
            attendanceEventId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId,
        string permission)
    {
        if (!StoreMatches(
                connection,
                transaction,
                storeId))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncident.StoreAuthorityMismatch",
                    "StoreId does not match local deployment authority."));

        return HasPermission(
                connection,
                transaction,
                actorUserId,
                permission)
            ? Result.Success()
            : Result.Fail(
                Error.Forbidden(
                    "CafeAttendanceIncident.PermissionRequired",
                    "Operation requires permission: " + permission));
    }

    private static bool StoreMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM deployment_identity
            WHERE store_id=$store;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool HasPermission(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorUserId,
        string permission)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp ON rp.role_id=u.role_id
            WHERE u.id=$user
              AND u.status=1
              AND rp.permission=$permission;
            """;
        command.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$permission", permission);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool ActiveUserExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users
            WHERE id=$user AND status=1;
            """;
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static void EnsureOperationUnused(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        string op = operationId.ToString("D");
        foreach (string sql in new[]
        {
            "SELECT COUNT(*) FROM cafe_attendance_events WHERE operation_id=$operation;",
            "SELECT COUNT(*) FROM cafe_attendance_corrections WHERE operation_id=$operation;",
            "SELECT COUNT(*) FROM cafe_incident_operations WHERE operation_id=$operation;"
        })
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$operation", op);
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0)
                throw new C10ValidationException(
                    "CafeAttendanceIncident.OperationCollision",
                    "OperationId is already owned by another C10 fact.");
        }
    }

    private static string? ValidateAttendanceMutation(
        CafeAttendanceRecordMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.AttendanceEventId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.SubjectUserId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty)
            return "Operation, event, store, subject, actor and device IDs are required.";

        if (!CafeAttendanceEventTypes.IsKnown(mutation.EventType))
            return "Attendance event type must be ClockIn or ClockOut.";

        return null;
    }

    private static string? ValidateCorrectionMutation(
        CafeAttendanceCorrectMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.AttendanceCorrectionId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.AttendanceEventId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty)
            return "Operation, correction, store, event, actor and device IDs are required.";

        if (!CafeAttendanceCorrectionKinds.IsKnown(mutation.CorrectionKind))
            return "Attendance correction kind must be Void or Replace.";

        if (string.IsNullOrWhiteSpace(mutation.Reason))
            return "Attendance correction reason is required.";

        bool replace = string.Equals(
            mutation.CorrectionKind,
            CafeAttendanceCorrectionKinds.Replace,
            StringComparison.Ordinal);

        if (replace &&
            (!mutation.ReplacementAttendanceEventId.HasValue ||
             mutation.ReplacementAttendanceEventId.Value == Guid.Empty ||
             !CafeAttendanceEventTypes.IsKnown(mutation.ReplacementEventType) ||
             !mutation.ReplacementOccurredAtUtc.HasValue))
            return "Replace correction requires replacement event identity, type and occurred time.";

        if (!replace &&
            (mutation.ReplacementAttendanceEventId.HasValue ||
             mutation.ReplacementEventType is not null ||
             mutation.ReplacementOccurredAtUtc.HasValue))
            return "Void correction cannot carry replacement attendance evidence.";

        return null;
    }

    private static DateTimeOffset ParseDate(string text) =>
        DateTimeOffset.Parse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static string Hash(
        params object?[] values)
    {
        string canonical = string.Join(
            "|",
            values.Select(x =>
                x switch
                {
                    null => "",
                    Guid id => id.ToString("D"),
                    _ => Convert.ToString(
                            x,
                            CultureInfo.InvariantCulture)
                        ?? ""
                }));

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private sealed record AttendanceBaseRow(
        Guid StoreId,
        Guid SubjectUserId);

    private sealed class C10ValidationException :
        Exception
    {
        public C10ValidationException(
            string code,
            string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
