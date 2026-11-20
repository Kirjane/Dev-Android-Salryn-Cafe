using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeAttendanceIncidentSchemaMigration : IDatabaseMigration
{
    public int Version => 39;

    public string Name => "Cafe attendance and incident operational evidence";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE cafe_attendance_events (
                attendance_event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                subject_user_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                FOREIGN KEY(subject_user_id) REFERENCES users(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(event_type IN ('ClockIn','ClockOut')),
                CHECK(LENGTH(payload_hash)=64),
                UNIQUE(operation_id)
            );

            CREATE TABLE cafe_attendance_corrections (
                attendance_correction_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                attendance_event_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                correction_kind TEXT NOT NULL,
                replacement_attendance_event_id TEXT NULL,
                reason TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                FOREIGN KEY(attendance_event_id)
                    REFERENCES cafe_attendance_events(attendance_event_id),
                FOREIGN KEY(replacement_attendance_event_id)
                    REFERENCES cafe_attendance_events(attendance_event_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(correction_kind IN ('Void','Replace')),
                CHECK(LENGTH(TRIM(reason))>0),
                CHECK(LENGTH(payload_hash)=64),
                CHECK(
                    (correction_kind='Void' AND replacement_attendance_event_id IS NULL)
                    OR
                    (correction_kind='Replace' AND replacement_attendance_event_id IS NOT NULL)
                ),
                UNIQUE(operation_id),
                UNIQUE(attendance_event_id)
            );

            CREATE TABLE cafe_incidents (
                incident_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                current_version INTEGER NOT NULL,
                current_revision_id TEXT NOT NULL,
                status TEXT NOT NULL,
                reporter_user_id TEXT NOT NULL,
                reporter_device_id TEXT NOT NULL,
                subject_user_id TEXT NULL,
                occurred_at_utc TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                resolved_by_user_id TEXT NULL,
                resolved_at_utc TEXT NULL,
                resolution_note TEXT NULL,
                FOREIGN KEY(reporter_user_id) REFERENCES users(id),
                FOREIGN KEY(subject_user_id) REFERENCES users(id),
                FOREIGN KEY(resolved_by_user_id) REFERENCES users(id),
                FOREIGN KEY(current_revision_id)
                    REFERENCES cafe_incident_revisions(revision_id)
                    DEFERRABLE INITIALLY DEFERRED,
                CHECK(current_version>0),
                CHECK(status IN ('Open','Resolved')),
                CHECK(
                    (status='Open'
                     AND resolved_by_user_id IS NULL
                     AND resolved_at_utc IS NULL
                     AND resolution_note IS NULL)
                    OR
                    (status='Resolved'
                     AND resolved_by_user_id IS NOT NULL
                     AND resolved_at_utc IS NOT NULL
                     AND resolution_note IS NOT NULL
                     AND LENGTH(TRIM(resolution_note))>0)
                )
            );

            CREATE TABLE cafe_incident_revisions (
                revision_id TEXT PRIMARY KEY,
                incident_id TEXT NOT NULL,
                revision_number INTEGER NOT NULL,
                incident_version INTEGER NOT NULL,
                previous_revision_id TEXT NULL,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                classification TEXT NOT NULL,
                severity TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(incident_id)
                    REFERENCES cafe_incidents(incident_id)
                    DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(previous_revision_id)
                    REFERENCES cafe_incident_revisions(revision_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(revision_number>0),
                CHECK(incident_version>0),
                CHECK(LENGTH(TRIM(title))>0),
                CHECK(LENGTH(TRIM(description))>0),
                CHECK(classification IN ('Operational','Safety','Conduct','Equipment','Other')),
                CHECK(severity IN ('Low','Medium','High','Critical')),
                CHECK(LENGTH(content_hash)=64),
                UNIQUE(incident_id,revision_number),
                UNIQUE(incident_id,incident_version)
            );

            CREATE TABLE cafe_incident_operations (
                operation_id TEXT PRIMARY KEY,
                incident_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                result_revision_id TEXT NULL,
                result_status TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY(incident_id) REFERENCES cafe_incidents(incident_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                FOREIGN KEY(result_revision_id) REFERENCES cafe_incident_revisions(revision_id),
                CHECK(operation_type IN (
                    'IncidentRevisionCommittedV1',
                    'IncidentResolvedV1')),
                CHECK(LENGTH(payload_hash)=64),
                CHECK(expected_version>=0),
                CHECK(resulting_version>0),
                CHECK(result_status IN ('Open','Resolved'))
            );

            CREATE TABLE cafe_incident_resolutions (
                resolution_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                incident_id TEXT NOT NULL UNIQUE,
                actor_user_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                resolution_note TEXT NOT NULL,
                resolved_at_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(operation_id) REFERENCES cafe_incident_operations(operation_id),
                FOREIGN KEY(incident_id) REFERENCES cafe_incidents(incident_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(LENGTH(TRIM(resolution_note))>0),
                CHECK(LENGTH(content_hash)=64)
            );

            CREATE TABLE cafe_attendance_incident_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                authority_digest TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence>=0),
                CHECK(LENGTH(snapshot_hash)=64),
                CHECK(LENGTH(authority_digest)=64)
            );

            CREATE INDEX idx_cafe_attendance_store_subject_time
                ON cafe_attendance_events(
                    store_id,subject_user_id,occurred_at_utc,attendance_event_id);

            CREATE INDEX idx_cafe_attendance_correction_event
                ON cafe_attendance_corrections(
                    attendance_event_id,occurred_at_utc,attendance_correction_id);

            CREATE INDEX idx_cafe_incidents_store_status_time
                ON cafe_incidents(
                    store_id,status,occurred_at_utc,incident_id);

            CREATE INDEX idx_cafe_incident_revisions_incident
                ON cafe_incident_revisions(
                    incident_id,incident_version,revision_number);

            CREATE INDEX idx_cafe_incident_operations_incident
                ON cafe_incident_operations(
                    incident_id,resulting_version,operation_id);

            CREATE TRIGGER trg_cafe_attendance_events_no_update
            BEFORE UPDATE ON cafe_attendance_events
            BEGIN
                SELECT RAISE(ABORT,'cafe_attendance_events is immutable');
            END;

            CREATE TRIGGER trg_cafe_attendance_events_no_delete
            BEFORE DELETE ON cafe_attendance_events
            BEGIN
                SELECT RAISE(ABORT,'cafe_attendance_events is immutable');
            END;

            CREATE TRIGGER trg_cafe_attendance_corrections_no_update
            BEFORE UPDATE ON cafe_attendance_corrections
            BEGIN
                SELECT RAISE(ABORT,'cafe_attendance_corrections is immutable');
            END;

            CREATE TRIGGER trg_cafe_attendance_corrections_no_delete
            BEFORE DELETE ON cafe_attendance_corrections
            BEGIN
                SELECT RAISE(ABORT,'cafe_attendance_corrections is immutable');
            END;

            CREATE TRIGGER trg_cafe_incidents_terminal_guard
            BEFORE UPDATE ON cafe_incidents
            WHEN OLD.status='Resolved'
            BEGIN
                SELECT RAISE(ABORT,'resolved cafe incident is terminal');
            END;

            CREATE TRIGGER trg_cafe_incidents_version_guard
            BEFORE UPDATE ON cafe_incidents
            WHEN NEW.current_version <> OLD.current_version + 1
              OR NEW.incident_id <> OLD.incident_id
              OR NEW.store_id <> OLD.store_id
              OR NEW.reporter_user_id <> OLD.reporter_user_id
              OR NEW.reporter_device_id <> OLD.reporter_device_id
              OR NEW.subject_user_id IS NOT OLD.subject_user_id
              OR NEW.occurred_at_utc <> OLD.occurred_at_utc
              OR NEW.created_at_utc <> OLD.created_at_utc
            BEGIN
                SELECT RAISE(ABORT,'cafe incident update must advance one version without rewriting identity');
            END;

            CREATE TRIGGER trg_cafe_incidents_no_delete
            BEFORE DELETE ON cafe_incidents
            BEGIN
                SELECT RAISE(ABORT,'cafe incidents cannot be deleted');
            END;

            CREATE TRIGGER trg_cafe_incident_revisions_no_update
            BEFORE UPDATE ON cafe_incident_revisions
            BEGIN
                SELECT RAISE(ABORT,'cafe_incident_revisions is immutable');
            END;

            CREATE TRIGGER trg_cafe_incident_revisions_no_delete
            BEFORE DELETE ON cafe_incident_revisions
            BEGIN
                SELECT RAISE(ABORT,'cafe_incident_revisions is immutable');
            END;

            CREATE TRIGGER trg_cafe_incident_operations_no_update
            BEFORE UPDATE ON cafe_incident_operations
            BEGIN
                SELECT RAISE(ABORT,'cafe_incident_operations is immutable');
            END;

            CREATE TRIGGER trg_cafe_incident_operations_no_delete
            BEFORE DELETE ON cafe_incident_operations
            BEGIN
                SELECT RAISE(ABORT,'cafe_incident_operations is immutable');
            END;

            CREATE TRIGGER trg_cafe_incident_resolutions_no_update
            BEFORE UPDATE ON cafe_incident_resolutions
            BEGIN
                SELECT RAISE(ABORT,'cafe_incident_resolutions is immutable');
            END;

            CREATE TRIGGER trg_cafe_incident_resolutions_no_delete
            BEFORE DELETE ON cafe_incident_resolutions
            BEGIN
                SELECT RAISE(ABORT,'cafe_incident_resolutions is immutable');
            END;
            """);
    }

    private static void Execute(
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
