using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class SyncClientOutboxFoundationMigration : IDatabaseMigration
{
    public int Version => 28;

    public string Name => "Sync client outbox foundation";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE sync_device_state (
                device_id TEXT PRIMARY KEY,
                next_local_sequence INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CHECK (next_local_sequence > 0)
            );

            CREATE TABLE sync_outbox (
                operation_id TEXT PRIMARY KEY,
                device_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                local_sequence INTEGER NOT NULL,
                protocol_version INTEGER NOT NULL,
                operation_type TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                aggregate_id TEXT NOT NULL,
                base_version INTEGER NULL,
                occurred_at_local TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                operation_hash TEXT NOT NULL,

                delivery_status TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                central_received_sequence INTEGER NULL,
                central_received_at_utc TEXT NULL,

                resolution_status TEXT NULL,
                resolution_code TEXT NULL,
                resolution_json TEXT NULL,
                resolved_at_utc TEXT NULL,

                created_at_utc TEXT NOT NULL,

                UNIQUE(device_id, local_sequence),
                CHECK (local_sequence > 0),
                CHECK (protocol_version = 1),
                CHECK (schema_version > 0),
                CHECK (delivery_status IN ('PendingUpload', 'ReceivedByCentral')),
                CHECK (resolution_status IS NULL OR resolution_status IN ('Applied', 'Rejected', 'NeedsReview')),
                CHECK (
                    (delivery_status = 'PendingUpload' AND central_received_sequence IS NULL AND central_received_at_utc IS NULL)
                    OR
                    (delivery_status = 'ReceivedByCentral' AND central_received_sequence IS NOT NULL AND central_received_at_utc IS NOT NULL)
                )
            );

            CREATE INDEX idx_sync_outbox_pending
                ON sync_outbox(device_id, delivery_status, local_sequence);

            CREATE TRIGGER trg_sync_outbox_immutable_envelope
            BEFORE UPDATE ON sync_outbox
            FOR EACH ROW
            WHEN
                NEW.operation_id IS NOT OLD.operation_id OR
                NEW.device_id IS NOT OLD.device_id OR
                NEW.store_id IS NOT OLD.store_id OR
                NEW.actor_user_id IS NOT OLD.actor_user_id OR
                NEW.local_sequence IS NOT OLD.local_sequence OR
                NEW.protocol_version IS NOT OLD.protocol_version OR
                NEW.operation_type IS NOT OLD.operation_type OR
                NEW.schema_version IS NOT OLD.schema_version OR
                NEW.aggregate_id IS NOT OLD.aggregate_id OR
                NEW.base_version IS NOT OLD.base_version OR
                NEW.occurred_at_local IS NOT OLD.occurred_at_local OR
                NEW.payload_json IS NOT OLD.payload_json OR
                NEW.payload_hash IS NOT OLD.payload_hash OR
                NEW.operation_hash IS NOT OLD.operation_hash OR
                NEW.created_at_utc IS NOT OLD.created_at_utc
            BEGIN
                SELECT RAISE(ABORT, 'sync_outbox immutable envelope fields cannot be changed');
            END;

            CREATE TRIGGER trg_sync_outbox_no_delete
            BEFORE DELETE ON sync_outbox
            FOR EACH ROW
            BEGIN
                SELECT RAISE(ABORT, 'sync_outbox evidence cannot be deleted');
            END;
            """;
        command.ExecuteNonQuery();
    }
}
