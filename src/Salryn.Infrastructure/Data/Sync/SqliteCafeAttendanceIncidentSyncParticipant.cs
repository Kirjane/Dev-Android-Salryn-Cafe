using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class SqliteCafeAttendanceIncidentSyncParticipant
{
    public static bool IsSupportedSourceOperation(string? operationType) =>
        CafeAttendanceIncidentSyncContract.IsSupported(operationType);

    public static string AppliedChangeTypeForSource(string operationType) =>
        operationType switch
        {
            CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1 =>
                AppliedBusinessChangeContract.AttendanceEventRecordedV1AppliedV1,
            CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1 =>
                AppliedBusinessChangeContract.AttendanceEventCorrectedV1AppliedV1,
            CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1 =>
                AppliedBusinessChangeContract.IncidentRevisionCommittedV1AppliedV1,
            CafeAttendanceIncidentSyncContract.IncidentResolvedV1 =>
                AppliedBusinessChangeContract.IncidentResolvedV1AppliedV1,
            _ => throw new InvalidOperationException(
                "Unsupported C10 semantic operation.")
        };

    public static string? SourceOperationTypeForApplied(string changeType) =>
        changeType switch
        {
            AppliedBusinessChangeContract.AttendanceEventRecordedV1AppliedV1 =>
                CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1,
            AppliedBusinessChangeContract.AttendanceEventCorrectedV1AppliedV1 =>
                CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1,
            AppliedBusinessChangeContract.IncidentRevisionCommittedV1AppliedV1 =>
                CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
            AppliedBusinessChangeContract.IncidentResolvedV1AppliedV1 =>
                CafeAttendanceIncidentSyncContract.IncidentResolvedV1,
            _ => null
        };

    public static Result ApplyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsSupportedSourceOperation(envelope.OperationType) ||
            envelope.SchemaVersion != CafeAttendanceIncidentSyncContract.SchemaVersion)
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.ContractUnsupported",
                    "Unsupported C10 operation type/schema."));

        if (!StoreMatches(connection, transaction, envelope.StoreId))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.StoreAuthorityMismatch",
                    "C10 StoreId does not match deployment authority."));

        if (!UserExists(connection, transaction, envelope.ActorUserId))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.ActorMissing",
                    "C10 actor prerequisite is missing."));

        try
        {
            return envelope.OperationType switch
            {
                CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1 =>
                    ApplyAttendanceRecorded(connection, transaction, envelope),
                CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1 =>
                    ApplyAttendanceCorrected(connection, transaction, envelope),
                CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1 =>
                    ApplyIncidentRevision(connection, transaction, envelope),
                CafeAttendanceIncidentSyncContract.IncidentResolvedV1 =>
                    ApplyIncidentResolved(connection, transaction, envelope),
                _ => Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.ContractUnsupported",
                        "Unsupported C10 operation."))
            };
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result.Fail(
                Error.Unexpected(
                    "CafeAttendanceIncidentSync.ApplyFailed",
                    ex.Message));
        }
    }

    public static bool VerifyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        try
        {
            return envelope.OperationType switch
            {
                CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1 =>
                    VerifyAttendanceRecorded(connection, transaction, envelope),
                CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1 =>
                    VerifyAttendanceCorrected(connection, transaction, envelope),
                CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1 =>
                    VerifyIncidentOperation(connection, transaction, envelope),
                CafeAttendanceIncidentSyncContract.IncidentResolvedV1 =>
                    VerifyIncidentOperation(connection, transaction, envelope),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static Result ApplyAttendanceRecorded(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var parsed =
            CafeAttendanceIncidentCanonicalJson.Parse<AttendanceEventRecordedV1>(
                envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
            return Invalid(parsed.ErrorCode, parsed.ErrorMessage);

        CafeAttendanceEventEvidenceV1 e = parsed.Payload.Event;
        if (envelope.OperationId != e.OperationId ||
            envelope.AggregateId != e.AttendanceEventId ||
            envelope.StoreId != e.StoreId ||
            envelope.ActorUserId != e.ActorUserId ||
            envelope.DeviceId != e.DeviceId ||
            envelope.BaseVersion is not null)
            return EnvelopeMismatch();

        if (!UserExists(connection, transaction, e.SubjectUserId))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.SubjectMissing",
                    "Attendance subject prerequisite is missing."));

        if (!CafeAttendanceEventTypes.IsKnown(e.EventType))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.AttendanceTypeInvalid",
                    "Attendance event type is invalid."));

        if (AttendanceOperationExists(connection, transaction, e.OperationId))
            return VerifyAttendanceRecorded(connection, transaction, envelope)
                ? Result.Success()
                : Collision();

        EnsureOperationUnused(connection, transaction, e.OperationId);

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
        command.Parameters.AddWithValue("$event", e.AttendanceEventId.ToString("D"));
        command.Parameters.AddWithValue("$operation", e.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$store", e.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$subject", e.SubjectUserId.ToString("D"));
        command.Parameters.AddWithValue("$actor", e.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$device", e.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$type", e.EventType);
        command.Parameters.AddWithValue("$occurred", e.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$recorded", e.RecordedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$hash", e.PayloadHash);
        command.ExecuteNonQuery();
        return Result.Success();
    }

    private static Result ApplyAttendanceCorrected(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var parsed =
            CafeAttendanceIncidentCanonicalJson.Parse<AttendanceEventCorrectedV1>(
                envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
            return Invalid(parsed.ErrorCode, parsed.ErrorMessage);

        AttendanceEventCorrectedV1 p = parsed.Payload;
        if (envelope.OperationId != p.OperationId ||
            envelope.AggregateId != p.AttendanceEventId ||
            envelope.StoreId != p.StoreId ||
            envelope.ActorUserId != p.ActorUserId ||
            envelope.DeviceId != p.DeviceId ||
            envelope.BaseVersion is not null)
            return EnvelopeMismatch();

        if (CorrectionOperationExists(connection, transaction, p.OperationId))
            return VerifyAttendanceCorrected(connection, transaction, envelope)
                ? Result.Success()
                : Collision();

        EnsureOperationUnused(connection, transaction, p.OperationId);

        AttendanceAuthority? original =
            ReadAttendance(
                connection,
                transaction,
                p.AttendanceEventId);
        if (original is null ||
            original.StoreId != p.StoreId)
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.AttendancePrerequisiteMissing",
                    "Correction requires same-store retained attendance fact."));

        if (AttendanceCorrected(
                connection,
                transaction,
                p.AttendanceEventId))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.AttendanceAlreadyCorrected",
                    "Attendance fact already has a direct correction."));

        if (!CafeAttendanceCorrectionKinds.IsKnown(p.CorrectionKind) ||
            string.IsNullOrWhiteSpace(p.Reason))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.CorrectionInvalid",
                    "Correction kind/reason is invalid."));

        bool replace = p.CorrectionKind == CafeAttendanceCorrectionKinds.Replace;
        if (replace != (p.ReplacementEvent is not null) ||
            replace != p.ReplacementAttendanceEventId.HasValue)
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.ReplacementShapeInvalid",
                    "Replace correction requires exact replacement evidence."));

        if (p.ReplacementEvent is not null)
        {
            CafeAttendanceEventEvidenceV1 r = p.ReplacementEvent;
            if (r.AttendanceEventId != p.ReplacementAttendanceEventId ||
                r.OperationId != p.OperationId ||
                r.StoreId != p.StoreId ||
                r.SubjectUserId != original.SubjectUserId ||
                r.ActorUserId != p.ActorUserId ||
                r.DeviceId != p.DeviceId ||
                !CafeAttendanceEventTypes.IsKnown(r.EventType))
                return Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.ReplacementAuthorityMismatch",
                        "Replacement attendance evidence is inconsistent."));

            using SqliteCommand insertReplacement = connection.CreateCommand();
            insertReplacement.Transaction = transaction;
            insertReplacement.CommandText = """
                INSERT INTO cafe_attendance_events(
                    attendance_event_id,operation_id,store_id,
                    subject_user_id,actor_user_id,device_id,event_type,
                    occurred_at_utc,recorded_at_utc,payload_hash)
                VALUES(
                    $event,$operation,$store,$subject,$actor,$device,$type,
                    $occurred,$recorded,$hash);
                """;
            insertReplacement.Parameters.AddWithValue("$event", r.AttendanceEventId.ToString("D"));
            insertReplacement.Parameters.AddWithValue("$operation", r.OperationId.ToString("D"));
            insertReplacement.Parameters.AddWithValue("$store", r.StoreId.ToString("D"));
            insertReplacement.Parameters.AddWithValue("$subject", r.SubjectUserId.ToString("D"));
            insertReplacement.Parameters.AddWithValue("$actor", r.ActorUserId.ToString("D"));
            insertReplacement.Parameters.AddWithValue("$device", r.DeviceId.ToString("D"));
            insertReplacement.Parameters.AddWithValue("$type", r.EventType);
            insertReplacement.Parameters.AddWithValue("$occurred", r.OccurredAtUtc.ToUniversalTime().ToString("O"));
            insertReplacement.Parameters.AddWithValue("$recorded", r.RecordedAtUtc.ToUniversalTime().ToString("O"));
            insertReplacement.Parameters.AddWithValue("$hash", r.PayloadHash);
            insertReplacement.ExecuteNonQuery();
        }

        using SqliteCommand command = connection.CreateCommand();
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
        command.Parameters.AddWithValue("$correction", p.AttendanceCorrectionId.ToString("D"));
        command.Parameters.AddWithValue("$operation", p.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$store", p.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$event", p.AttendanceEventId.ToString("D"));
        command.Parameters.AddWithValue("$actor", p.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$device", p.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$kind", p.CorrectionKind);
        command.Parameters.AddWithValue(
            "$replacement",
            p.ReplacementAttendanceEventId.HasValue
                ? p.ReplacementAttendanceEventId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue("$reason", p.Reason.Trim());
        command.Parameters.AddWithValue("$at", p.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$hash", p.PayloadHash);
        command.ExecuteNonQuery();
        return Result.Success();
    }

    private static Result ApplyIncidentRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var parsed =
            CafeAttendanceIncidentCanonicalJson.Parse<IncidentRevisionCommittedV1>(
                envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
            return Invalid(parsed.ErrorCode, parsed.ErrorMessage);

        IncidentRevisionCommittedV1 p = parsed.Payload;
        if (envelope.OperationId != p.OperationId ||
            envelope.AggregateId != p.IncidentId ||
            envelope.StoreId != p.StoreId ||
            envelope.ActorUserId != p.ActorUserId ||
            envelope.DeviceId != p.DeviceId ||
            envelope.BaseVersion != p.ExpectedVersion)
            return EnvelopeMismatch();

        if (IncidentOperationExists(connection, transaction, p.OperationId))
            return VerifyIncidentOperation(connection, transaction, envelope)
                ? Result.Success()
                : Collision();

        EnsureOperationUnused(connection, transaction, p.OperationId);

        if (!UserExists(connection, transaction, p.ReporterUserId) ||
            (p.SubjectUserId.HasValue &&
             !UserExists(connection, transaction, p.SubjectUserId.Value)))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.IncidentUserPrerequisiteMissing",
                    "Incident reporter/subject prerequisite is missing."));

        CafeIncidentRevisionEvidenceV1 r = p.Revision;
        if (r.RevisionId == Guid.Empty ||
            r.IncidentId != p.IncidentId ||
            r.IncidentVersion != p.ResultingVersion ||
            r.ActorUserId != p.ActorUserId ||
            r.DeviceId != p.DeviceId ||
            r.CreatedAtUtc.ToUniversalTime() !=
                p.OccurredAtUtc.ToUniversalTime() ||
            !string.Equals(
                r.ContentHash,
                p.PayloadHash,
                StringComparison.Ordinal) ||
            !CafeIncidentClassifications.IsKnown(r.Classification) ||
            !CafeIncidentSeverities.IsKnown(r.Severity) ||
            string.IsNullOrWhiteSpace(r.Title) ||
            string.IsNullOrWhiteSpace(r.Description))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.IncidentRevisionInvalid",
                    "Incident revision authority is invalid."));

        IncidentAuthority? existing =
            ReadIncident(connection, transaction, p.IncidentId);

        if (p.ExpectedVersion == 0)
        {
            if (existing is not null ||
                p.ResultingVersion != 1 ||
                r.RevisionNumber != 1 ||
                r.PreviousRevisionId is not null ||
                p.ReporterUserId != p.ActorUserId ||
                p.ReporterDeviceId != p.DeviceId)
                return Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.IncidentCreateConflict",
                        "Incident create authority conflicts with retained state."));

            using SqliteCommand root = connection.CreateCommand();
            root.Transaction = transaction;
            root.CommandText = """
                INSERT INTO cafe_incidents(
                    incident_id,store_id,current_version,current_revision_id,
                    status,reporter_user_id,reporter_device_id,subject_user_id,
                    occurred_at_utc,created_at_utc,updated_at_utc,
                    resolved_by_user_id,resolved_at_utc,resolution_note)
                VALUES(
                    $incident,$store,1,$revision,'Open',$reporter,$device,$subject,
                    $incidentAt,$at,$at,NULL,NULL,NULL);
                """;
            root.Parameters.AddWithValue("$incident", p.IncidentId.ToString("D"));
            root.Parameters.AddWithValue("$store", p.StoreId.ToString("D"));
            root.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
            root.Parameters.AddWithValue("$reporter", p.ReporterUserId.ToString("D"));
            root.Parameters.AddWithValue("$device", p.ReporterDeviceId.ToString("D"));
            root.Parameters.AddWithValue(
                "$subject",
                p.SubjectUserId.HasValue ? p.SubjectUserId.Value.ToString("D") : DBNull.Value);
            root.Parameters.AddWithValue("$incidentAt", p.IncidentOccurredAtUtc.ToUniversalTime().ToString("O"));
            root.Parameters.AddWithValue("$at", p.OccurredAtUtc.ToUniversalTime().ToString("O"));
            root.ExecuteNonQuery();
        }
        else
        {
            if (existing is null ||
                existing.StoreId != p.StoreId ||
                existing.Version != p.ExpectedVersion ||
                existing.Status != CafeIncidentStatuses.Open ||
                r.PreviousRevisionId != existing.CurrentRevisionId ||
                p.ResultingVersion != p.ExpectedVersion + 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.IncidentExpectedVersionConflict",
                        "Incident revision prerequisite/version is inconsistent."));

            if (existing.ReporterUserId != p.ReporterUserId ||
                existing.ReporterDeviceId != p.ReporterDeviceId ||
                existing.SubjectUserId != p.SubjectUserId ||
                existing.IncidentOccurredAtUtc.ToUniversalTime() !=
                    p.IncidentOccurredAtUtc.ToUniversalTime() ||
                r.RevisionNumber != existing.CurrentRevisionNumber + 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.IncidentProvenanceConflict",
                        "Incident revision cannot rewrite retained provenance or skip revision continuity."));

            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_incidents
                SET current_version=$version,
                    current_revision_id=$revision,
                    updated_at_utc=$at
                WHERE incident_id=$incident
                  AND current_version=$expected
                  AND status='Open';
                """;
            update.Parameters.AddWithValue("$version", p.ResultingVersion);
            update.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
            update.Parameters.AddWithValue("$at", p.OccurredAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$incident", p.IncidentId.ToString("D"));
            update.Parameters.AddWithValue("$expected", p.ExpectedVersion);
            if (update.ExecuteNonQuery() != 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.IncidentConcurrentConflict",
                        "Incident revision lost update ownership."));
        }

        InsertRevision(connection, transaction, r);
        InsertIncidentOperation(
            connection,
            transaction,
            p.OperationId,
            p.IncidentId,
            p.StoreId,
            p.DeviceId,
            p.ActorUserId,
            CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
            p.PayloadHash,
            p.ExpectedVersion,
            p.ResultingVersion,
            r.RevisionId,
            CafeIncidentStatuses.Open,
            p.OccurredAtUtc);

        return Result.Success();
    }

    private static Result ApplyIncidentResolved(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var parsed =
            CafeAttendanceIncidentCanonicalJson.Parse<IncidentResolvedV1>(
                envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
            return Invalid(parsed.ErrorCode, parsed.ErrorMessage);

        IncidentResolvedV1 p = parsed.Payload;
        if (envelope.OperationId != p.OperationId ||
            envelope.AggregateId != p.IncidentId ||
            envelope.StoreId != p.StoreId ||
            envelope.ActorUserId != p.ActorUserId ||
            envelope.DeviceId != p.DeviceId ||
            envelope.BaseVersion != p.ExpectedVersion)
            return EnvelopeMismatch();

        if (IncidentOperationExists(connection, transaction, p.OperationId))
            return VerifyIncidentOperation(connection, transaction, envelope)
                ? Result.Success()
                : Collision();

        EnsureOperationUnused(connection, transaction, p.OperationId);

        IncidentAuthority? existing =
            ReadIncident(connection, transaction, p.IncidentId);
        if (existing is null ||
            existing.StoreId != p.StoreId ||
            existing.Version != p.ExpectedVersion ||
            existing.Status != CafeIncidentStatuses.Open ||
            existing.CurrentRevisionId != p.CurrentRevisionId ||
            p.ResultingVersion != p.ExpectedVersion + 1 ||
            string.IsNullOrWhiteSpace(p.ResolutionNote))
            return Result.Fail(
                Error.Conflict(
                    "CafeAttendanceIncidentSync.IncidentResolvePrerequisiteConflict",
                    "Incident resolution prerequisite/version is inconsistent."));

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_incidents
                SET current_version=$version,
                    status='Resolved',
                    updated_at_utc=$at,
                    resolved_by_user_id=$actor,
                    resolved_at_utc=$at,
                    resolution_note=$note
                WHERE incident_id=$incident
                  AND current_version=$expected
                  AND status='Open';
                """;
            update.Parameters.AddWithValue("$version", p.ResultingVersion);
            update.Parameters.AddWithValue("$at", p.ResolvedAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$actor", p.ActorUserId.ToString("D"));
            update.Parameters.AddWithValue("$note", p.ResolutionNote.Trim());
            update.Parameters.AddWithValue("$incident", p.IncidentId.ToString("D"));
            update.Parameters.AddWithValue("$expected", p.ExpectedVersion);
            if (update.ExecuteNonQuery() != 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeAttendanceIncidentSync.IncidentConcurrentConflict",
                        "Incident resolution lost update ownership."));
        }

        InsertIncidentOperation(
            connection,
            transaction,
            p.OperationId,
            p.IncidentId,
            p.StoreId,
            p.DeviceId,
            p.ActorUserId,
            CafeAttendanceIncidentSyncContract.IncidentResolvedV1,
            p.PayloadHash,
            p.ExpectedVersion,
            p.ResultingVersion,
            p.CurrentRevisionId,
            CafeIncidentStatuses.Resolved,
            p.ResolvedAtUtc);

        using SqliteCommand resolution = connection.CreateCommand();
        resolution.Transaction = transaction;
        resolution.CommandText = """
            INSERT INTO cafe_incident_resolutions(
                resolution_id,operation_id,incident_id,actor_user_id,
                device_id,resolution_note,resolved_at_utc,content_hash)
            VALUES(
                $resolution,$operation,$incident,$actor,$device,$note,$at,$hash);
            """;
        resolution.Parameters.AddWithValue("$resolution", p.ResolutionId.ToString("D"));
        resolution.Parameters.AddWithValue("$operation", p.OperationId.ToString("D"));
        resolution.Parameters.AddWithValue("$incident", p.IncidentId.ToString("D"));
        resolution.Parameters.AddWithValue("$actor", p.ActorUserId.ToString("D"));
        resolution.Parameters.AddWithValue("$device", p.DeviceId.ToString("D"));
        resolution.Parameters.AddWithValue("$note", p.ResolutionNote.Trim());
        resolution.Parameters.AddWithValue("$at", p.ResolvedAtUtc.ToUniversalTime().ToString("O"));
        resolution.Parameters.AddWithValue("$hash", p.PayloadHash);
        resolution.ExecuteNonQuery();

        return Result.Success();
    }

    private static bool VerifyAttendanceRecorded(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var parsed =
            CafeAttendanceIncidentCanonicalJson.Parse<AttendanceEventRecordedV1>(
                envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
            return false;

        CafeAttendanceEventEvidenceV1 e = parsed.Payload.Event;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attendance_event_id,store_id,subject_user_id,actor_user_id,
                   device_id,event_type,occurred_at_utc,recorded_at_utc,payload_hash
            FROM cafe_attendance_events
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() &&
            Guid.Parse(reader.GetString(0)) == e.AttendanceEventId &&
            Guid.Parse(reader.GetString(1)) == e.StoreId &&
            Guid.Parse(reader.GetString(2)) == e.SubjectUserId &&
            Guid.Parse(reader.GetString(3)) == e.ActorUserId &&
            Guid.Parse(reader.GetString(4)) == e.DeviceId &&
            reader.GetString(5) == e.EventType &&
            DateTimeOffset.Parse(reader.GetString(6)).ToUniversalTime() == e.OccurredAtUtc.ToUniversalTime() &&
            DateTimeOffset.Parse(reader.GetString(7)).ToUniversalTime() == e.RecordedAtUtc.ToUniversalTime() &&
            reader.GetString(8) == e.PayloadHash;
    }

    private static bool VerifyAttendanceCorrected(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var parsed =
            CafeAttendanceIncidentCanonicalJson.Parse<AttendanceEventCorrectedV1>(
                envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
            return false;

        AttendanceEventCorrectedV1 p = parsed.Payload;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attendance_correction_id,store_id,attendance_event_id,
                   actor_user_id,device_id,correction_kind,
                   replacement_attendance_event_id,reason,occurred_at_utc,payload_hash
            FROM cafe_attendance_corrections
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        Guid? replacement =
            reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6));

        bool correctionMatches =
            Guid.Parse(reader.GetString(0)) == p.AttendanceCorrectionId &&
            Guid.Parse(reader.GetString(1)) == p.StoreId &&
            Guid.Parse(reader.GetString(2)) == p.AttendanceEventId &&
            Guid.Parse(reader.GetString(3)) == p.ActorUserId &&
            Guid.Parse(reader.GetString(4)) == p.DeviceId &&
            reader.GetString(5) == p.CorrectionKind &&
            replacement == p.ReplacementAttendanceEventId &&
            reader.GetString(7) == p.Reason.Trim() &&
            DateTimeOffset.Parse(reader.GetString(8)).ToUniversalTime() == p.OccurredAtUtc.ToUniversalTime() &&
            reader.GetString(9) == p.PayloadHash;

        if (!correctionMatches)
            return false;

        return p.ReplacementEvent is null ||
            VerifyReplacement(connection, transaction, p.ReplacementEvent);
    }

    private static bool VerifyReplacement(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeAttendanceEventEvidenceV1 e)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id,subject_user_id,actor_user_id,device_id,event_type,
                   occurred_at_utc,recorded_at_utc,payload_hash
            FROM cafe_attendance_events
            WHERE attendance_event_id=$event
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$event", e.AttendanceEventId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() &&
            Guid.Parse(reader.GetString(0)) == e.StoreId &&
            Guid.Parse(reader.GetString(1)) == e.SubjectUserId &&
            Guid.Parse(reader.GetString(2)) == e.ActorUserId &&
            Guid.Parse(reader.GetString(3)) == e.DeviceId &&
            reader.GetString(4) == e.EventType &&
            DateTimeOffset.Parse(reader.GetString(5)).ToUniversalTime() == e.OccurredAtUtc.ToUniversalTime() &&
            DateTimeOffset.Parse(reader.GetString(6)).ToUniversalTime() == e.RecordedAtUtc.ToUniversalTime() &&
            reader.GetString(7) == e.PayloadHash;
    }

    private static bool VerifyIncidentOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        string expectedHash;
        Guid incidentId;
        int expectedVersion;
        int resultingVersion;
        Guid revisionId;
        string status;

        if (envelope.OperationType ==
            CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1)
        {
            var parsed =
                CafeAttendanceIncidentCanonicalJson.Parse<IncidentRevisionCommittedV1>(
                    envelope.PayloadJson);
            if (!parsed.IsValid || parsed.Payload is null)
                return false;
            var p = parsed.Payload;
            expectedHash = p.PayloadHash;
            incidentId = p.IncidentId;
            expectedVersion = p.ExpectedVersion;
            resultingVersion = p.ResultingVersion;
            revisionId = p.Revision.RevisionId;
            status = CafeIncidentStatuses.Open;
        }
        else
        {
            var parsed =
                CafeAttendanceIncidentCanonicalJson.Parse<IncidentResolvedV1>(
                    envelope.PayloadJson);
            if (!parsed.IsValid || parsed.Payload is null)
                return false;
            var p = parsed.Payload;
            expectedHash = p.PayloadHash;
            incidentId = p.IncidentId;
            expectedVersion = p.ExpectedVersion;
            resultingVersion = p.ResultingVersion;
            revisionId = p.CurrentRevisionId;
            status = CafeIncidentStatuses.Resolved;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT incident_id,store_id,device_id,actor_user_id,operation_type,
                   payload_hash,expected_version,resulting_version,
                   result_revision_id,result_status
            FROM cafe_incident_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() &&
            Guid.Parse(reader.GetString(0)) == incidentId &&
            Guid.Parse(reader.GetString(1)) == envelope.StoreId &&
            Guid.Parse(reader.GetString(2)) == envelope.DeviceId &&
            Guid.Parse(reader.GetString(3)) == envelope.ActorUserId &&
            reader.GetString(4) == envelope.OperationType &&
            reader.GetString(5) == expectedHash &&
            reader.GetInt32(6) == expectedVersion &&
            reader.GetInt32(7) == resultingVersion &&
            Guid.Parse(reader.GetString(8)) == revisionId &&
            reader.GetString(9) == status;
    }

    private static AttendanceAuthority? ReadAttendance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid eventId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id,subject_user_id
            FROM cafe_attendance_events
            WHERE attendance_event_id=$event
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$event", eventId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)))
            : null;
    }

    private static IncidentAuthority? ReadIncident(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid incidentId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                i.store_id,i.current_version,i.current_revision_id,i.status,
                i.reporter_user_id,i.reporter_device_id,i.subject_user_id,
                i.occurred_at_utc,r.revision_number
            FROM cafe_incidents i
            INNER JOIN cafe_incident_revisions r
              ON r.revision_id=i.current_revision_id
            WHERE i.incident_id=$incident
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$incident", incidentId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                Guid.Parse(reader.GetString(4)),
                Guid.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                DateTimeOffset.Parse(reader.GetString(7)).ToUniversalTime(),
                reader.GetInt32(8))
            : null;
    }

    private static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeIncidentRevisionEvidenceV1 r)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_incident_revisions(
                revision_id,incident_id,revision_number,incident_version,
                previous_revision_id,title,description,classification,severity,
                actor_user_id,device_id,created_at_utc,content_hash)
            VALUES(
                $revision,$incident,$number,$version,$previous,$title,$description,
                $classification,$severity,$actor,$device,$created,$hash);
            """;
        command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
        command.Parameters.AddWithValue("$incident", r.IncidentId.ToString("D"));
        command.Parameters.AddWithValue("$number", r.RevisionNumber);
        command.Parameters.AddWithValue("$version", r.IncidentVersion);
        command.Parameters.AddWithValue(
            "$previous",
            r.PreviousRevisionId.HasValue ? r.PreviousRevisionId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$title", r.Title.Trim());
        command.Parameters.AddWithValue("$description", r.Description.Trim());
        command.Parameters.AddWithValue("$classification", r.Classification);
        command.Parameters.AddWithValue("$severity", r.Severity);
        command.Parameters.AddWithValue("$actor", r.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$device", r.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$created", r.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$hash", r.ContentHash);
        command.ExecuteNonQuery();
    }

    private static void InsertIncidentOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid incidentId,
        Guid storeId,
        Guid deviceId,
        Guid actorUserId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        int resultingVersion,
        Guid revisionId,
        string status,
        DateTimeOffset occurredAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_incident_operations(
                operation_id,incident_id,store_id,device_id,actor_user_id,
                operation_type,payload_hash,expected_version,resulting_version,
                result_revision_id,result_status,occurred_at_utc)
            VALUES(
                $operation,$incident,$store,$device,$actor,$type,$hash,
                $expected,$resulting,$revision,$status,$at);
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$incident", incidentId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$type", operationType);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$at", occurredAt.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool AttendanceOperationExists(
        SqliteConnection c,
        SqliteTransaction t,
        Guid operationId) =>
        Exists(c, t, "cafe_attendance_events", "operation_id", operationId);

    private static bool CorrectionOperationExists(
        SqliteConnection c,
        SqliteTransaction t,
        Guid operationId) =>
        Exists(c, t, "cafe_attendance_corrections", "operation_id", operationId);

    private static bool IncidentOperationExists(
        SqliteConnection c,
        SqliteTransaction t,
        Guid operationId) =>
        Exists(c, t, "cafe_incident_operations", "operation_id", operationId);

    private static bool AttendanceCorrected(
        SqliteConnection c,
        SqliteTransaction t,
        Guid eventId) =>
        Exists(c, t, "cafe_attendance_corrections", "attendance_event_id", eventId);

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        Guid id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM " + table +
            " WHERE " + column + "=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static bool StoreMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM deployment_identity WHERE store_id=$store;";
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool UserExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM users WHERE id=$user AND status=1;";
        command.Parameters.AddWithValue("$user", userId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static void EnsureOperationUnused(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        if (AttendanceOperationExists(connection, transaction, operationId) ||
            CorrectionOperationExists(connection, transaction, operationId) ||
            IncidentOperationExists(connection, transaction, operationId))
            throw new InvalidOperationException(
                "C10 OperationId is already owned by another semantic fact.");
    }

    private static Result Invalid(string? code, string? message) =>
        Result.Fail(
            Error.Conflict(
                code ?? "CafeAttendanceIncidentSync.PayloadInvalid",
                message ?? "C10 payload is invalid."));

    private static Result EnvelopeMismatch() =>
        Result.Fail(
            Error.Conflict(
                "CafeAttendanceIncidentSync.EnvelopeAuthorityMismatch",
                "C10 envelope identity/base-version differs from canonical payload."));

    private static Result Collision() =>
        Result.Fail(
            Error.Conflict(
                "CafeAttendanceIncidentSync.OperationCollision",
                "C10 OperationId already exists with different canonical evidence."));

    private sealed record AttendanceAuthority(
        Guid StoreId,
        Guid SubjectUserId);

    private sealed record IncidentAuthority(
        Guid StoreId,
        int Version,
        Guid CurrentRevisionId,
        string Status,
        Guid ReporterUserId,
        Guid ReporterDeviceId,
        Guid? SubjectUserId,
        DateTimeOffset IncidentOccurredAtUtc,
        int CurrentRevisionNumber);
}
