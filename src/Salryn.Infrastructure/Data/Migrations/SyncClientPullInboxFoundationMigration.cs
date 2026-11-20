using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class SyncClientPullInboxFoundationMigration : IDatabaseMigration
{
    public int Version => 29;

    public string Name => "Client pull inbox and remote cursor foundation";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE sync_remote_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NULL,
                last_received_server_sequence INTEGER NOT NULL DEFAULT 0,
                last_applied_server_sequence INTEGER NOT NULL DEFAULT 0,
                state TEXT NOT NULL,
                last_error TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CHECK (last_received_server_sequence >= 0),
                CHECK (last_applied_server_sequence >= 0),
                CHECK (last_applied_server_sequence <= last_received_server_sequence),
                CHECK (state IN (
                    'Uninitialized',
                    'Ready',
                    'BootstrapRequired',
                    'ReconciliationRequired'))
            );

            CREATE TABLE sync_change_inbox (
                server_sequence INTEGER PRIMARY KEY,
                change_id TEXT NOT NULL UNIQUE,
                central_instance_id TEXT NOT NULL,
                source_operation_id TEXT NOT NULL,
                origin_device_id TEXT NOT NULL,
                origin_local_sequence INTEGER NOT NULL,
                store_id TEXT NOT NULL,
                change_type TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                aggregate_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                change_hash TEXT NOT NULL,
                change_created_at_utc TEXT NOT NULL,
                received_at_utc TEXT NOT NULL,
                apply_status TEXT NOT NULL,
                apply_error TEXT NULL,
                applied_at_utc TEXT NULL,
                CHECK (server_sequence > 0),
                CHECK (origin_local_sequence > 0),
                CHECK (schema_version > 0),
                CHECK (length(apply_status) > 0)
            );

            CREATE INDEX idx_sync_change_inbox_store_status_sequence
                ON sync_change_inbox(store_id, apply_status, server_sequence);

            CREATE TRIGGER trg_sync_change_inbox_immutable_evidence
            BEFORE UPDATE ON sync_change_inbox
            FOR EACH ROW
            WHEN
                NEW.server_sequence IS NOT OLD.server_sequence OR
                NEW.change_id IS NOT OLD.change_id OR
                NEW.central_instance_id IS NOT OLD.central_instance_id OR
                NEW.source_operation_id IS NOT OLD.source_operation_id OR
                NEW.origin_device_id IS NOT OLD.origin_device_id OR
                NEW.origin_local_sequence IS NOT OLD.origin_local_sequence OR
                NEW.store_id IS NOT OLD.store_id OR
                NEW.change_type IS NOT OLD.change_type OR
                NEW.schema_version IS NOT OLD.schema_version OR
                NEW.aggregate_id IS NOT OLD.aggregate_id OR
                NEW.payload_json IS NOT OLD.payload_json OR
                NEW.payload_hash IS NOT OLD.payload_hash OR
                NEW.change_hash IS NOT OLD.change_hash OR
                NEW.change_created_at_utc IS NOT OLD.change_created_at_utc OR
                NEW.received_at_utc IS NOT OLD.received_at_utc
            BEGIN
                SELECT RAISE(ABORT, 'sync_change_inbox immutable evidence cannot be changed');
            END;

            CREATE TRIGGER trg_sync_change_inbox_no_delete
            BEFORE DELETE ON sync_change_inbox
            FOR EACH ROW
            BEGIN
                SELECT RAISE(ABORT, 'sync_change_inbox evidence cannot be deleted');
            END;
            """;
        command.ExecuteNonQuery();
    }
}
