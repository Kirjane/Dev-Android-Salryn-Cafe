using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeOrderSchemaMigration : IDatabaseMigration
{
    public int Version => 33;

    public string Name => "Cafe order lifecycle preparation and sale correlation";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE cafe_orders (
                order_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                current_version INTEGER NOT NULL,
                current_revision_id TEXT NOT NULL,
                menu_publication_revision_id TEXT NOT NULL,
                state TEXT NOT NULL,
                service_mode TEXT NOT NULL,
                service_reference TEXT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                submitted_revision_id TEXT NULL,
                submitted_recipe_book_revision_id TEXT NULL,
                completed_sale_id TEXT NULL UNIQUE,
                cancellation_reason TEXT NULL,
                FOREIGN KEY(menu_publication_revision_id) REFERENCES menu_revisions(revision_id),
                FOREIGN KEY(submitted_recipe_book_revision_id) REFERENCES recipe_book_revisions(revision_id),
                FOREIGN KEY(completed_sale_id) REFERENCES sales(id),
                FOREIGN KEY(created_by_user_id) REFERENCES users(id),
                CHECK(current_version > 0),
                CHECK(state IN (
                    'Open','Parked','Submitted','Preparing','Ready','Completed','Cancelled')),
                CHECK(service_mode IN ('DineIn','Takeout')),
                CHECK(
                    (state='Completed' AND completed_sale_id IS NOT NULL)
                    OR (state<>'Completed' AND completed_sale_id IS NULL)
                ),
                CHECK(
                    (state='Cancelled' AND cancellation_reason IS NOT NULL)
                    OR (state<>'Cancelled')
                ),
                CHECK(
                    state IN ('Open','Parked','Cancelled')
                    OR submitted_revision_id IS NOT NULL
                )
            );

            CREATE TABLE cafe_order_revisions (
                revision_id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL,
                revision_number INTEGER NOT NULL,
                order_version INTEGER NOT NULL,
                previous_revision_id TEXT NULL,
                menu_publication_revision_id TEXT NOT NULL,
                service_mode TEXT NOT NULL,
                service_reference TEXT NULL,
                content_hash TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(previous_revision_id) REFERENCES cafe_order_revisions(revision_id),
                FOREIGN KEY(menu_publication_revision_id) REFERENCES menu_revisions(revision_id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(revision_number > 0),
                CHECK(order_version > 0),
                CHECK(service_mode IN ('DineIn','Takeout')),
                UNIQUE(order_id, revision_number),
                UNIQUE(order_id, order_version),
                UNIQUE(order_id, content_hash)
            );

            CREATE TABLE cafe_order_revision_lines (
                revision_id TEXT NOT NULL,
                order_line_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name_snapshot TEXT NOT NULL,
                unit_name_snapshot TEXT NOT NULL,
                quantity_x1000 INTEGER NOT NULL,
                base_unit_price_cents INTEGER NOT NULL,
                variant_id TEXT NULL,
                variant_name_snapshot TEXT NULL,
                variant_unit_price_cents INTEGER NULL,
                note TEXT NULL,
                calculated_unit_price_cents INTEGER NOT NULL,
                line_total_cents INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY(revision_id, order_line_id),
                FOREIGN KEY(revision_id) REFERENCES cafe_order_revisions(revision_id),
                CHECK(quantity_x1000 > 0),
                CHECK(base_unit_price_cents >= 0),
                CHECK(variant_unit_price_cents IS NULL OR variant_unit_price_cents >= 0),
                CHECK(calculated_unit_price_cents >= 0),
                CHECK(line_total_cents >= 0),
                CHECK(
                    (variant_id IS NULL AND variant_name_snapshot IS NULL AND variant_unit_price_cents IS NULL)
                    OR
                    (variant_id IS NOT NULL AND variant_name_snapshot IS NOT NULL AND variant_unit_price_cents IS NOT NULL)
                )
            );

            CREATE TABLE cafe_order_revision_modifiers (
                revision_id TEXT NOT NULL,
                order_line_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                group_name_snapshot TEXT NOT NULL,
                option_id TEXT NOT NULL,
                option_name_snapshot TEXT NOT NULL,
                price_delta_cents INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY(revision_id, order_line_id, option_id),
                FOREIGN KEY(revision_id, order_line_id)
                    REFERENCES cafe_order_revision_lines(revision_id, order_line_id)
            );

            CREATE TABLE cafe_order_operations (
                operation_id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                result_revision_id TEXT NULL,
                result_state TEXT NOT NULL,
                result_sale_id TEXT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(result_revision_id) REFERENCES cafe_order_revisions(revision_id),
                FOREIGN KEY(result_sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(result_state IN (
                    'Open','Parked','Submitted','Preparing','Ready','Completed','Cancelled'))
            );

            CREATE TABLE cafe_order_lifecycle_events (
                event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                order_id TEXT NOT NULL,
                expected_version INTEGER NOT NULL,
                resulting_version INTEGER NOT NULL,
                action TEXT NOT NULL,
                from_state TEXT NULL,
                to_state TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                reason TEXT NULL,
                order_revision_id TEXT NULL,
                recipe_book_revision_id TEXT NULL,
                sale_id TEXT NULL,
                content_hash TEXT NOT NULL,
                FOREIGN KEY(operation_id) REFERENCES cafe_order_operations(operation_id),
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(order_revision_id) REFERENCES cafe_order_revisions(revision_id),
                FOREIGN KEY(recipe_book_revision_id) REFERENCES recipe_book_revisions(revision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(expected_version >= 0),
                CHECK(resulting_version > 0),
                CHECK(action IN (
                    'Created','Revised','Parked','Resumed','PreparationSubmitted',
                    'PreparationPreparing','PreparationReady','Cancelled','Completed')),
                CHECK(to_state IN (
                    'Open','Parked','Submitted','Preparing','Ready','Completed','Cancelled'))
            );

            CREATE TABLE cafe_order_sale_links (
                order_id TEXT PRIMARY KEY,
                order_revision_id TEXT NOT NULL,
                sale_id TEXT NOT NULL UNIQUE,
                completion_operation_id TEXT NOT NULL UNIQUE,
                sale_committed_operation_id TEXT NOT NULL UNIQUE,
                completed_at_utc TEXT NOT NULL,
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(order_revision_id) REFERENCES cafe_order_revisions(revision_id),
                FOREIGN KEY(sale_id) REFERENCES sales(id)
            );

            CREATE TABLE sale_item_order_links (
                sale_item_id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL,
                order_revision_id TEXT NOT NULL,
                order_line_id TEXT NOT NULL,
                FOREIGN KEY(sale_item_id) REFERENCES sale_items(id),
                FOREIGN KEY(order_id) REFERENCES cafe_orders(order_id),
                FOREIGN KEY(order_revision_id, order_line_id)
                    REFERENCES cafe_order_revision_lines(revision_id, order_line_id),
                UNIQUE(order_id, order_revision_id, order_line_id)
            );

            CREATE TABLE cafe_order_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence >= 0)
            );

            CREATE INDEX idx_cafe_orders_store_state_updated
                ON cafe_orders(store_id, state, updated_at_utc, order_id);

            CREATE INDEX idx_cafe_order_revisions_order_version
                ON cafe_order_revisions(order_id, order_version, revision_number);

            CREATE INDEX idx_cafe_order_lifecycle_order_version
                ON cafe_order_lifecycle_events(order_id, resulting_version, occurred_at_utc);

            CREATE INDEX idx_cafe_order_operations_order
                ON cafe_order_operations(order_id, resulting_version, operation_id);
            """);

        foreach (string table in new[]
        {
            "cafe_order_revisions",
            "cafe_order_revision_lines",
            "cafe_order_revision_modifiers",
            "cafe_order_operations",
            "cafe_order_lifecycle_events",
            "cafe_order_sale_links",
            "sale_item_order_links"
        })
        {
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table + "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table + "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
        }

        Execute(connection, transaction, """
            CREATE TRIGGER trg_cafe_orders_terminal_guard
            BEFORE UPDATE ON cafe_orders
            FOR EACH ROW
            WHEN OLD.state IN ('Completed','Cancelled')
            BEGIN
                SELECT RAISE(ABORT, 'terminal cafe order is immutable');
            END;

            CREATE TRIGGER trg_cafe_orders_no_delete
            BEFORE DELETE ON cafe_orders
            BEGIN
                SELECT RAISE(ABORT, 'cafe order cannot be deleted');
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
