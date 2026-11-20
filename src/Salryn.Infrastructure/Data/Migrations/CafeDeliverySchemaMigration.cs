using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeDeliverySchemaMigration : IDatabaseMigration
{
    public int Version => 37;

    public string Name => "Cafe delivery fulfillment authority";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // C8 activates Delivery as an accepted persisted C5 fee type.
        // SQLite cannot widen a CHECK constraint in-place, so migration 37
        // rebuilds only cafe_sale_fees while preserving existing C5 evidence.
        Execute(connection, transaction, """
            DROP TRIGGER IF EXISTS trg_cafe_sale_fees_no_update;
            DROP TRIGGER IF EXISTS trg_cafe_sale_fees_no_delete;
            DROP INDEX IF EXISTS idx_cafe_sale_fees_sale;

            ALTER TABLE cafe_sale_fees
                RENAME TO cafe_sale_fees_c8_legacy;

            CREATE TABLE cafe_sale_fees (
                fee_id TEXT PRIMARY KEY,
                financial_decision_id TEXT NOT NULL,
                sale_id TEXT NOT NULL,
                fee_type TEXT NOT NULL,
                display_label_snapshot TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                actor_user_id TEXT NOT NULL,
                reason TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(financial_decision_id)
                    REFERENCES cafe_sale_financial_decisions(financial_decision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(fee_type IN ('Service','Delivery')),
                CHECK(amount_cents >= 0)
            );

            INSERT INTO cafe_sale_fees(
                fee_id,
                financial_decision_id,
                sale_id,
                fee_type,
                display_label_snapshot,
                amount_cents,
                actor_user_id,
                reason,
                created_at_utc)
            SELECT
                fee_id,
                financial_decision_id,
                sale_id,
                fee_type,
                display_label_snapshot,
                amount_cents,
                actor_user_id,
                reason,
                created_at_utc
            FROM cafe_sale_fees_c8_legacy;

            DROP TABLE cafe_sale_fees_c8_legacy;

            CREATE INDEX idx_cafe_sale_fees_sale
                ON cafe_sale_fees(sale_id, fee_id);

            CREATE TRIGGER trg_cafe_sale_fees_no_update
            BEFORE UPDATE ON cafe_sale_fees
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'cafe_sale_fees is immutable');
            END;

            CREATE TRIGGER trg_cafe_sale_fees_no_delete
            BEFORE DELETE ON cafe_sale_fees
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'cafe_sale_fees is immutable');
            END;
            """);
        Execute(connection, transaction, """
            CREATE TABLE cafe_delivery_fulfillments (
                delivery_id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL UNIQUE,
                store_id TEXT NOT NULL,
                current_version INTEGER NOT NULL,
                current_revision_id TEXT NOT NULL,
                status TEXT NOT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                terminal_reason TEXT NULL,
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(created_by_user_id) REFERENCES users(id),
                FOREIGN KEY(current_revision_id)
                    REFERENCES cafe_delivery_revisions(revision_id)
                    DEFERRABLE INITIALLY DEFERRED,
                CHECK(current_version > 0),
                CHECK(status IN (
                    'Pending','Dispatched','Delivered','Failed','Cancelled')),
                CHECK(
                    (status IN ('Failed','Cancelled')
                     AND terminal_reason IS NOT NULL
                     AND LENGTH(TRIM(terminal_reason)) > 0)
                    OR
                    (status NOT IN ('Failed','Cancelled')
                     AND terminal_reason IS NULL)
                )
            );

            CREATE TABLE cafe_delivery_revisions (
                revision_id TEXT PRIMARY KEY,
                delivery_id TEXT NOT NULL,
                order_id TEXT NOT NULL,
                revision_number INTEGER NOT NULL,
                delivery_version INTEGER NOT NULL,
                recipient_name TEXT NOT NULL,
                contact_phone TEXT NULL,
                delivery_address TEXT NOT NULL,
                landmark TEXT NULL,
                instructions TEXT NULL,
                delivery_fee_cents INTEGER NOT NULL,
                courier_reference TEXT NULL,
                content_hash TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(delivery_id)
                    REFERENCES cafe_delivery_fulfillments(delivery_id)
                    DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(revision_number > 0),
                CHECK(delivery_version > 0),
                CHECK(LENGTH(TRIM(recipient_name)) > 0),
                CHECK(LENGTH(TRIM(delivery_address)) > 0),
                CHECK(delivery_fee_cents >= 0),
                CHECK(LENGTH(content_hash) = 64),
                UNIQUE(delivery_id, revision_number),
                UNIQUE(delivery_id, delivery_version),
                UNIQUE(delivery_id, content_hash)
            );

            CREATE TABLE cafe_delivery_operations (
                operation_id TEXT PRIMARY KEY,
                delivery_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                result_revision_id TEXT NULL,
                result_status TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY(delivery_id) REFERENCES cafe_delivery_fulfillments(delivery_id),
                FOREIGN KEY(result_revision_id) REFERENCES cafe_delivery_revisions(revision_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(LENGTH(payload_hash) = 64),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(operation_type IN (
                    'DeliveryCreatedV1',
                    'DeliveryRevisedV1',
                    'DeliveryDispatchedV1',
                    'DeliveryResolvedV1',
                    'DeliveryCancelledV1')),
                CHECK(result_status IN (
                    'Pending','Dispatched','Delivered','Failed','Cancelled'))
            );

            CREATE TABLE cafe_delivery_events (
                event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                delivery_id TEXT NOT NULL,
                order_id TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                action TEXT NOT NULL,
                from_status TEXT NULL,
                to_status TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                reason TEXT NULL,
                delivery_revision_id TEXT NULL,
                sale_id TEXT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(operation_id) REFERENCES cafe_delivery_operations(operation_id),
                FOREIGN KEY(delivery_id) REFERENCES cafe_delivery_fulfillments(delivery_id),
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(delivery_revision_id) REFERENCES cafe_delivery_revisions(revision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(action IN (
                    'Created','Revised','Dispatched','Delivered','Failed','Cancelled')),
                CHECK(to_status IN (
                    'Pending','Dispatched','Delivered','Failed','Cancelled')),
                CHECK(LENGTH(content_hash) = 64)
            );

            CREATE TABLE cafe_delivery_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence >= 0),
                CHECK(LENGTH(snapshot_hash) = 64)
            );

            CREATE INDEX idx_cafe_delivery_store_status
                ON cafe_delivery_fulfillments(
                    store_id,status,updated_at_utc,delivery_id);

            CREATE INDEX idx_cafe_delivery_revision_delivery
                ON cafe_delivery_revisions(
                    delivery_id,delivery_version,revision_number);

            CREATE INDEX idx_cafe_delivery_operation_delivery
                ON cafe_delivery_operations(
                    delivery_id,resulting_version,operation_id);

            CREATE INDEX idx_cafe_delivery_event_delivery
                ON cafe_delivery_events(
                    delivery_id,resulting_version,occurred_at_utc);

            CREATE TRIGGER trg_cafe_delivery_insert_order_guard
            BEFORE INSERT ON cafe_delivery_fulfillments
            BEGIN
                SELECT CASE
                    WHEN NOT EXISTS(
                        SELECT 1
                        FROM cafe_orders o
                        WHERE o.order_id=NEW.order_id
                          AND o.store_id=NEW.store_id
                          AND o.service_mode='Takeout'
                          AND o.state NOT IN ('Completed','Cancelled'))
                    THEN RAISE(
                        ABORT,
                        'delivery requires same-store active C4 Takeout order')
                END;
            END;

            CREATE TRIGGER trg_cafe_delivery_terminal_guard
            BEFORE UPDATE ON cafe_delivery_fulfillments
            WHEN OLD.status IN ('Delivered','Failed','Cancelled')
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'terminal cafe delivery is immutable');
            END;

            CREATE TRIGGER trg_cafe_delivery_version_guard
            BEFORE UPDATE ON cafe_delivery_fulfillments
            WHEN NEW.current_version <> OLD.current_version + 1
              OR NEW.delivery_id <> OLD.delivery_id
              OR NEW.order_id <> OLD.order_id
              OR NEW.store_id <> OLD.store_id
              OR NEW.created_by_user_id <> OLD.created_by_user_id
              OR NEW.created_at_utc <> OLD.created_at_utc
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'cafe delivery update must advance one version without rewriting identity');
            END;

            CREATE TRIGGER trg_cafe_delivery_no_delete
            BEFORE DELETE ON cafe_delivery_fulfillments
            BEGIN
                SELECT RAISE(
                    ABORT,
                    'cafe delivery cannot be deleted');
            END;
            """);

        foreach (string table in new[]
        {
            "cafe_delivery_revisions",
            "cafe_delivery_operations",
            "cafe_delivery_events"
        })
        {
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table +
                "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" +
                table + " is immutable'); END;");

            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table +
                "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" +
                table + " is immutable'); END;");
        }
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
