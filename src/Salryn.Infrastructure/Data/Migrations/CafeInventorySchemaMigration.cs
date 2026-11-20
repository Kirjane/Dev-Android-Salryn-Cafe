using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafeInventorySchemaMigration : IDatabaseMigration
{
    public int Version => 32;

    public string Name => "Cafe inventory purchasing waste physical count and supplier publication";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        RebuildIngredientMovementLedger(connection, transaction);

        Execute(connection, transaction, """
            CREATE TABLE supplier_directory_revisions (
                revision_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                revision_number INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                publication_hash TEXT NOT NULL,
                published_at_utc TEXT NOT NULL,
                UNIQUE(store_id, revision_number),
                UNIQUE(store_id, publication_hash)
            );

            CREATE TABLE supplier_directory_revision_items (
                revision_id TEXT NOT NULL,
                supplier_id TEXT NOT NULL,
                name TEXT NOT NULL,
                contact_number TEXT NULL,
                notes TEXT NULL,
                is_active INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY(revision_id, supplier_id),
                FOREIGN KEY(revision_id) REFERENCES supplier_directory_revisions(revision_id),
                CHECK(is_active IN (0,1))
            );

            CREATE TABLE supplier_directory_current_publication (
                store_id TEXT PRIMARY KEY,
                revision_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                publication_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                FOREIGN KEY(revision_id) REFERENCES supplier_directory_revisions(revision_id)
            );

            CREATE TABLE purchase_receipts (
                purchase_receipt_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                store_id TEXT NOT NULL,
                supplier_id TEXT NOT NULL,
                supplier_name_snapshot TEXT NOT NULL,
                external_reference TEXT NULL,
                received_at_utc TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                notes TEXT NULL,
                total_acquisition_cost_cents INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                correction_of_purchase_receipt_id TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(supplier_id) REFERENCES suppliers(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                FOREIGN KEY(correction_of_purchase_receipt_id) REFERENCES purchase_receipts(purchase_receipt_id)
            );

            CREATE TABLE purchase_receipt_lines (
                line_id TEXT PRIMARY KEY,
                purchase_receipt_id TEXT NOT NULL,
                stock_target_type TEXT NOT NULL,
                stock_target_id TEXT NOT NULL,
                name_snapshot TEXT NOT NULL,
                stock_unit_snapshot TEXT NOT NULL,
                quantity_received_x1000 INTEGER NOT NULL,
                purchase_package_label TEXT NULL,
                purchase_package_quantity_x1000 INTEGER NULL,
                purchase_package_content_x1000 INTEGER NULL,
                line_acquisition_cost_cents INTEGER NOT NULL,
                FOREIGN KEY(purchase_receipt_id) REFERENCES purchase_receipts(purchase_receipt_id),
                CHECK(stock_target_type IN ('Ingredient','Product')),
                CHECK(quantity_received_x1000 <> 0),
                CHECK(purchase_package_quantity_x1000 IS NULL OR purchase_package_quantity_x1000 > 0),
                CHECK(purchase_package_content_x1000 IS NULL OR purchase_package_content_x1000 > 0)
            );

            CREATE INDEX idx_purchase_receipts_supplier_time
                ON purchase_receipts(supplier_id, received_at_utc, purchase_receipt_id);
            CREATE INDEX idx_purchase_receipt_lines_target
                ON purchase_receipt_lines(stock_target_type, stock_target_id, purchase_receipt_id);

            CREATE TABLE inventory_waste_events (
                waste_event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                store_id TEXT NOT NULL,
                reason TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                notes TEXT NULL,
                content_hash TEXT NOT NULL,
                correction_of_waste_event_id TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                FOREIGN KEY(correction_of_waste_event_id) REFERENCES inventory_waste_events(waste_event_id),
                CHECK(reason IN ('Waste','Spoilage','Damage','Expired'))
            );

            CREATE TABLE inventory_waste_lines (
                line_id TEXT PRIMARY KEY,
                waste_event_id TEXT NOT NULL,
                stock_target_type TEXT NOT NULL,
                stock_target_id TEXT NOT NULL,
                name_snapshot TEXT NOT NULL,
                stock_unit_snapshot TEXT NOT NULL,
                quantity_wasted_x1000 INTEGER NOT NULL,
                FOREIGN KEY(waste_event_id) REFERENCES inventory_waste_events(waste_event_id),
                CHECK(stock_target_type IN ('Ingredient','Product')),
                CHECK(quantity_wasted_x1000 > 0)
            );

            CREATE INDEX idx_inventory_waste_target
                ON inventory_waste_lines(stock_target_type, stock_target_id, waste_event_id);

            CREATE TABLE inventory_adjustment_events (
                adjustment_event_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                store_id TEXT NOT NULL,
                stock_target_type TEXT NOT NULL,
                stock_target_id TEXT NOT NULL,
                name_snapshot TEXT NOT NULL,
                stock_unit_snapshot TEXT NOT NULL,
                quantity_delta_x1000 INTEGER NOT NULL,
                reason TEXT NOT NULL,
                reference_type TEXT NULL,
                reference_id TEXT NULL,
                correction_of_reference_id TEXT NULL,
                actor_user_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(stock_target_type IN ('Ingredient','Product')),
                CHECK(quantity_delta_x1000 <> 0)
            );

            CREATE INDEX idx_inventory_adjustment_target
                ON inventory_adjustment_events(stock_target_type, stock_target_id, occurred_at_utc);

            CREATE TABLE inventory_count_sessions (
                count_session_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                state TEXT NOT NULL,
                started_by_user_id TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                finalized_by_user_id TEXT NULL,
                finalized_at_utc TEXT NULL,
                notes TEXT NULL,
                finalize_operation_id TEXT NULL UNIQUE,
                finalized_content_hash TEXT NULL,
                FOREIGN KEY(started_by_user_id) REFERENCES users(id),
                FOREIGN KEY(finalized_by_user_id) REFERENCES users(id),
                CHECK(state IN ('Draft','Finalized')),
                CHECK(
                    (state='Draft' AND finalized_by_user_id IS NULL AND finalized_at_utc IS NULL
                         AND finalize_operation_id IS NULL AND finalized_content_hash IS NULL)
                    OR
                    (state='Finalized' AND finalized_by_user_id IS NOT NULL AND finalized_at_utc IS NOT NULL
                         AND finalize_operation_id IS NOT NULL AND finalized_content_hash IS NOT NULL)
                )
            );

            CREATE TABLE inventory_count_lines (
                line_id TEXT PRIMARY KEY,
                count_session_id TEXT NOT NULL,
                stock_target_type TEXT NOT NULL,
                stock_target_id TEXT NOT NULL,
                name_snapshot TEXT NOT NULL,
                stock_unit_snapshot TEXT NOT NULL,
                expected_before_x1000 INTEGER NOT NULL,
                counted_x1000 INTEGER NOT NULL,
                quantity_delta_x1000 INTEGER NOT NULL,
                FOREIGN KEY(count_session_id) REFERENCES inventory_count_sessions(count_session_id),
                CHECK(stock_target_type IN ('Ingredient','Product')),
                CHECK(expected_before_x1000 >= 0),
                CHECK(counted_x1000 >= 0),
                UNIQUE(count_session_id, stock_target_type, stock_target_id)
            );

            CREATE TABLE cafe_inventory_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                supplier_revision_id TEXT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK(baseline_server_sequence >= 0)
            );

            CREATE INDEX idx_inventory_count_state
                ON inventory_count_sessions(store_id, state, started_at_utc);
            """);

        foreach (string table in new[]
        {
            "supplier_directory_revisions",
            "supplier_directory_revision_items",
            "purchase_receipts",
            "purchase_receipt_lines",
            "inventory_waste_events",
            "inventory_waste_lines",
            "inventory_adjustment_events"
        })
        {
            Execute(connection, transaction,
                "CREATE TRIGGER trg_" + table + "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
            Execute(connection, transaction,
                "CREATE TRIGGER trg_" + table + "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
        }

        Execute(connection, transaction, """
            CREATE TRIGGER trg_inventory_count_sessions_finalized_no_update
            BEFORE UPDATE ON inventory_count_sessions
            FOR EACH ROW WHEN OLD.state='Finalized'
            BEGIN
                SELECT RAISE(ABORT, 'finalized inventory count is immutable');
            END;

            CREATE TRIGGER trg_inventory_count_sessions_finalized_no_delete
            BEFORE DELETE ON inventory_count_sessions
            FOR EACH ROW WHEN OLD.state='Finalized'
            BEGIN
                SELECT RAISE(ABORT, 'finalized inventory count cannot be deleted');
            END;

            CREATE TRIGGER trg_inventory_count_lines_finalized_no_update
            BEFORE UPDATE ON inventory_count_lines
            FOR EACH ROW
            WHEN EXISTS(
                SELECT 1 FROM inventory_count_sessions s
                WHERE s.count_session_id=OLD.count_session_id AND s.state='Finalized')
            BEGIN
                SELECT RAISE(ABORT, 'finalized inventory count lines are immutable');
            END;

            CREATE TRIGGER trg_inventory_count_lines_finalized_no_delete
            BEFORE DELETE ON inventory_count_lines
            FOR EACH ROW
            WHEN EXISTS(
                SELECT 1 FROM inventory_count_sessions s
                WHERE s.count_session_id=OLD.count_session_id AND s.state='Finalized')
            BEGIN
                SELECT RAISE(ABORT, 'finalized inventory count lines cannot be deleted');
            END;
            """);
    }

    private static void RebuildIngredientMovementLedger(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            DROP TRIGGER IF EXISTS trg_ingredient_stock_movements_no_update;
            DROP TRIGGER IF EXISTS trg_ingredient_stock_movements_no_delete;
            DROP INDEX IF EXISTS idx_ingredient_consumption_movement_once;
            DROP INDEX IF EXISTS idx_ingredient_consumption_reversal_once;
            DROP INDEX IF EXISTS idx_ingredient_movements_ingredient_time;

            ALTER TABLE ingredient_stock_movements RENAME TO ingredient_stock_movements_c2;

            CREATE TABLE ingredient_stock_movements (
                id TEXT PRIMARY KEY,
                ingredient_id TEXT NOT NULL,
                sale_id TEXT NULL,
                sale_item_id TEXT NULL,
                consumption_line_id TEXT NULL,
                original_consumption_line_id TEXT NULL,
                reference_type TEXT NULL,
                reference_id TEXT NULL,
                reference_line_id TEXT NULL,
                correction_of_reference_id TEXT NULL,
                movement_kind TEXT NOT NULL,
                quantity_delta_x1000 INTEGER NOT NULL,
                actor_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(ingredient_id) REFERENCES ingredients(id),
                FOREIGN KEY(sale_id) REFERENCES sales(id),
                FOREIGN KEY(sale_item_id) REFERENCES sale_items(id),
                FOREIGN KEY(consumption_line_id) REFERENCES sale_ingredient_consumption_lines(id),
                FOREIGN KEY(original_consumption_line_id) REFERENCES sale_ingredient_consumption_lines(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                CHECK(movement_kind IN (
                    'RecipeConsumption','RecipeConsumptionReversal',
                    'PurchaseReceiving','PurchaseCorrection',
                    'Waste','WasteCorrection','CountCorrection','ManualAdjustment')),
                CHECK(quantity_delta_x1000 <> 0)
            );

            INSERT INTO ingredient_stock_movements(
                id, ingredient_id, sale_id, sale_item_id,
                consumption_line_id, original_consumption_line_id,
                reference_type, reference_id, reference_line_id, correction_of_reference_id,
                movement_kind, quantity_delta_x1000, actor_user_id, created_at_utc)
            SELECT
                id, ingredient_id, sale_id, sale_item_id,
                consumption_line_id, original_consumption_line_id,
                'Sale', sale_id,
                COALESCE(consumption_line_id, original_consumption_line_id), NULL,
                movement_kind, quantity_delta_x1000, actor_user_id, created_at_utc
            FROM ingredient_stock_movements_c2;

            DROP TABLE ingredient_stock_movements_c2;

            CREATE UNIQUE INDEX idx_ingredient_consumption_movement_once
                ON ingredient_stock_movements(consumption_line_id, movement_kind)
                WHERE consumption_line_id IS NOT NULL AND movement_kind='RecipeConsumption';

            CREATE UNIQUE INDEX idx_ingredient_consumption_reversal_once
                ON ingredient_stock_movements(original_consumption_line_id, movement_kind)
                WHERE original_consumption_line_id IS NOT NULL AND movement_kind='RecipeConsumptionReversal';

            CREATE INDEX idx_ingredient_movements_ingredient_time
                ON ingredient_stock_movements(ingredient_id, created_at_utc, id);

            CREATE INDEX idx_ingredient_movements_reference
                ON ingredient_stock_movements(reference_type, reference_id, reference_line_id);

            CREATE TRIGGER trg_ingredient_stock_movements_no_update
            BEFORE UPDATE ON ingredient_stock_movements
            BEGIN
                SELECT RAISE(ABORT, 'ingredient_stock_movements is immutable');
            END;

            CREATE TRIGGER trg_ingredient_stock_movements_no_delete
            BEFORE DELETE ON ingredient_stock_movements
            BEGIN
                SELECT RAISE(ABORT, 'ingredient_stock_movements is immutable');
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
