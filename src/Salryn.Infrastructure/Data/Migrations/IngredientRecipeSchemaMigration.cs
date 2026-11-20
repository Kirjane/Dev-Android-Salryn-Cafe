using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class IngredientRecipeSchemaMigration : IDatabaseMigration
{
    public int Version => 31;

    public string Name => "Cafe ingredients recipes BOM consumption and recipe-book publication";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE ingredients (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                stock_unit TEXT NOT NULL,
                stock_on_hand_x1000 INTEGER NOT NULL DEFAULT 0,
                reorder_level_x1000 INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                notes TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NULL,
                CHECK (stock_unit IN ('Gram','Milliliter','Piece')),
                CHECK (stock_on_hand_x1000 >= 0),
                CHECK (reorder_level_x1000 >= 0),
                CHECK (is_active IN (0,1))
            );

            CREATE TABLE recipes (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                yield_x1000 INTEGER NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NULL,
                CHECK (yield_x1000 > 0),
                CHECK (is_active IN (0,1))
            );

            CREATE TABLE recipe_lines (
                recipe_id TEXT NOT NULL,
                ingredient_id TEXT NOT NULL,
                input_unit TEXT NOT NULL,
                declared_quantity_x1000 INTEGER NOT NULL,
                normalized_quantity_x1000 INTEGER NOT NULL,
                display_order INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (recipe_id, ingredient_id),
                FOREIGN KEY (recipe_id) REFERENCES recipes(id),
                FOREIGN KEY (ingredient_id) REFERENCES ingredients(id),
                CHECK (input_unit IN ('Gram','Kilogram','Milliliter','Liter','Piece')),
                CHECK (declared_quantity_x1000 > 0),
                CHECK (normalized_quantity_x1000 > 0)
            );

            CREATE TABLE recipe_item_bindings (
                product_id TEXT PRIMARY KEY,
                recipe_id TEXT NOT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id),
                FOREIGN KEY (recipe_id) REFERENCES recipes(id)
            );

            CREATE TABLE recipe_variant_bindings (
                variant_id TEXT PRIMARY KEY,
                recipe_id TEXT NOT NULL,
                FOREIGN KEY (variant_id) REFERENCES menu_variants(id),
                FOREIGN KEY (recipe_id) REFERENCES recipes(id)
            );

            CREATE TABLE recipe_modifier_option_bindings (
                option_id TEXT PRIMARY KEY,
                recipe_id TEXT NOT NULL,
                FOREIGN KEY (option_id) REFERENCES menu_modifier_options(id),
                FOREIGN KEY (recipe_id) REFERENCES recipes(id)
            );

            CREATE TABLE recipe_book_revisions (
                revision_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                revision_number INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                publication_hash TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                published_at_utc TEXT NOT NULL,
                superseded_at_utc TEXT NULL,
                CHECK (revision_number > 0),
                CHECK (status IN ('Published','Superseded')),
                UNIQUE (store_id, revision_number),
                UNIQUE (store_id, publication_hash)
            );

            CREATE TABLE recipe_book_revision_ingredients (
                revision_id TEXT NOT NULL,
                ingredient_id TEXT NOT NULL,
                name TEXT NOT NULL,
                stock_unit TEXT NOT NULL,
                reorder_level_x1000 INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, ingredient_id),
                FOREIGN KEY (revision_id) REFERENCES recipe_book_revisions(revision_id),
                CHECK (stock_unit IN ('Gram','Milliliter','Piece')),
                CHECK (reorder_level_x1000 >= 0)
            );

            CREATE TABLE recipe_book_revision_recipes (
                revision_id TEXT NOT NULL,
                recipe_id TEXT NOT NULL,
                recipe_revision_id TEXT NOT NULL,
                name TEXT NOT NULL,
                yield_x1000 INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, recipe_id),
                UNIQUE (revision_id, recipe_revision_id),
                FOREIGN KEY (revision_id) REFERENCES recipe_book_revisions(revision_id),
                CHECK (yield_x1000 > 0)
            );

            CREATE TABLE recipe_book_revision_lines (
                revision_id TEXT NOT NULL,
                recipe_revision_id TEXT NOT NULL,
                recipe_id TEXT NOT NULL,
                ingredient_id TEXT NOT NULL,
                stock_unit TEXT NOT NULL,
                normalized_quantity_x1000 INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, recipe_id, ingredient_id),
                FOREIGN KEY (revision_id) REFERENCES recipe_book_revisions(revision_id),
                CHECK (stock_unit IN ('Gram','Milliliter','Piece')),
                CHECK (normalized_quantity_x1000 > 0)
            );

            CREATE TABLE recipe_book_revision_bindings (
                revision_id TEXT NOT NULL,
                binding_type TEXT NOT NULL,
                binding_id TEXT NOT NULL,
                recipe_id TEXT NOT NULL,
                recipe_revision_id TEXT NOT NULL,
                PRIMARY KEY (revision_id, binding_type, binding_id),
                FOREIGN KEY (revision_id) REFERENCES recipe_book_revisions(revision_id),
                CHECK (binding_type IN ('Item','Variant','ModifierOption'))
            );

            CREATE TABLE recipe_book_current_publication (
                store_id TEXT PRIMARY KEY,
                revision_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                publication_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                FOREIGN KEY (revision_id) REFERENCES recipe_book_revisions(revision_id)
            );

            CREATE TABLE sale_ingredient_consumption_decisions (
                sale_id TEXT PRIMARY KEY,
                source_sale_operation_id TEXT NULL,
                recipe_book_revision_id TEXT NULL,
                decision_hash TEXT NOT NULL,
                result TEXT NOT NULL,
                line_count INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (sale_id) REFERENCES sales(id),
                CHECK (result IN ('APPLIED','NO_RECIPE_APPLIED')),
                CHECK (line_count >= 0)
            );

            CREATE TABLE sale_ingredient_consumption_lines (
                id TEXT PRIMARY KEY,
                sale_id TEXT NOT NULL,
                sale_item_id TEXT NOT NULL,
                ingredient_id TEXT NOT NULL,
                recipe_id TEXT NOT NULL,
                recipe_revision_id TEXT NOT NULL,
                source_binding_type TEXT NOT NULL,
                source_binding_id TEXT NOT NULL,
                quantity_consumed_x1000 INTEGER NOT NULL,
                stock_unit TEXT NOT NULL,
                FOREIGN KEY (sale_id) REFERENCES sale_ingredient_consumption_decisions(sale_id),
                FOREIGN KEY (sale_item_id) REFERENCES sale_items(id),
                FOREIGN KEY (ingredient_id) REFERENCES ingredients(id),
                CHECK (source_binding_type IN ('Item','Variant','ModifierOption')),
                CHECK (quantity_consumed_x1000 > 0),
                CHECK (stock_unit IN ('Gram','Milliliter','Piece'))
            );

            CREATE INDEX idx_sale_ingredient_lines_sale
                ON sale_ingredient_consumption_lines(sale_id, sale_item_id);

            CREATE TABLE ingredient_stock_movements (
                id TEXT PRIMARY KEY,
                ingredient_id TEXT NOT NULL,
                sale_id TEXT NOT NULL,
                sale_item_id TEXT NULL,
                consumption_line_id TEXT NULL,
                original_consumption_line_id TEXT NULL,
                movement_kind TEXT NOT NULL,
                quantity_delta_x1000 INTEGER NOT NULL,
                actor_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (ingredient_id) REFERENCES ingredients(id),
                FOREIGN KEY (sale_id) REFERENCES sales(id),
                FOREIGN KEY (sale_item_id) REFERENCES sale_items(id),
                FOREIGN KEY (consumption_line_id) REFERENCES sale_ingredient_consumption_lines(id),
                FOREIGN KEY (original_consumption_line_id) REFERENCES sale_ingredient_consumption_lines(id),
                FOREIGN KEY (actor_user_id) REFERENCES users(id),
                CHECK (movement_kind IN ('RecipeConsumption','RecipeConsumptionReversal')),
                CHECK (quantity_delta_x1000 <> 0)
            );

            CREATE UNIQUE INDEX idx_ingredient_consumption_movement_once
                ON ingredient_stock_movements(consumption_line_id, movement_kind)
                WHERE consumption_line_id IS NOT NULL AND movement_kind='RecipeConsumption';

            CREATE UNIQUE INDEX idx_ingredient_consumption_reversal_once
                ON ingredient_stock_movements(original_consumption_line_id, movement_kind)
                WHERE original_consumption_line_id IS NOT NULL AND movement_kind='RecipeConsumptionReversal';

            CREATE INDEX idx_ingredient_movements_ingredient_time
                ON ingredient_stock_movements(ingredient_id, created_at_utc, id);

            CREATE TABLE cafe_ingredient_bootstrap_state (
                store_id TEXT PRIMARY KEY,
                central_instance_id TEXT NOT NULL,
                baseline_server_sequence INTEGER NOT NULL,
                snapshot_id TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                CHECK (baseline_server_sequence >= 0)
            );
            """);

        string[] immutableTables =
        [
            "recipe_book_revision_ingredients",
            "recipe_book_revision_recipes",
            "recipe_book_revision_lines",
            "recipe_book_revision_bindings",
            "sale_ingredient_consumption_decisions",
            "sale_ingredient_consumption_lines",
            "ingredient_stock_movements"
        ];

        foreach (string table in immutableTables)
        {
            Execute(connection, transaction,
                "CREATE TRIGGER trg_" + table + "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
            Execute(connection, transaction,
                "CREATE TRIGGER trg_" + table + "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, '" + table + " is immutable'); END;");
        }

        Execute(connection, transaction, """
            CREATE TRIGGER trg_recipe_book_revisions_immutable_identity
            BEFORE UPDATE ON recipe_book_revisions
            FOR EACH ROW
            WHEN NEW.revision_id IS NOT OLD.revision_id
              OR NEW.store_id IS NOT OLD.store_id
              OR NEW.revision_number IS NOT OLD.revision_number
              OR NEW.content_hash IS NOT OLD.content_hash
              OR NEW.publication_hash IS NOT OLD.publication_hash
              OR NEW.created_at_utc IS NOT OLD.created_at_utc
              OR NEW.published_at_utc IS NOT OLD.published_at_utc
            BEGIN
                SELECT RAISE(ABORT, 'recipe book revision identity/content is immutable');
            END;

            CREATE TRIGGER trg_recipe_book_revisions_no_delete
            BEFORE DELETE ON recipe_book_revisions
            BEGIN
                SELECT RAISE(ABORT, 'recipe book revision cannot be deleted');
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
