using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class MenuCatalogSchemaMigration : IDatabaseMigration
{
    public int Version => 30;

    public string Name => "Cafe menu catalog and immutable publication foundation";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE menu_item_availability (
                product_id TEXT PRIMARY KEY,
                is_temporarily_unavailable INTEGER NOT NULL DEFAULT 0,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id),
                CHECK (is_temporarily_unavailable IN (0, 1))
            );

            CREATE TABLE menu_variants (
                id TEXT PRIMARY KEY,
                product_id TEXT NOT NULL,
                name TEXT NOT NULL,
                sku TEXT NULL,
                barcode TEXT NULL,
                selling_price_cents INTEGER NOT NULL,
                cost_price_cents INTEGER NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_temporarily_unavailable INTEGER NOT NULL DEFAULT 0,
                display_order INTEGER NOT NULL DEFAULT 0,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id),
                CHECK (selling_price_cents >= 0),
                CHECK (cost_price_cents IS NULL OR cost_price_cents >= 0),
                CHECK (is_active IN (0, 1)),
                CHECK (is_temporarily_unavailable IN (0, 1)),
                UNIQUE (product_id, name)
            );

            CREATE INDEX idx_menu_variants_product_order
                ON menu_variants(product_id, display_order, name);

            CREATE UNIQUE INDEX idx_menu_variants_barcode_active
                ON menu_variants(barcode)
                WHERE barcode IS NOT NULL AND is_active = 1;

            CREATE TABLE menu_modifier_groups (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                is_required INTEGER NOT NULL DEFAULT 0,
                min_selections INTEGER NOT NULL DEFAULT 0,
                max_selections INTEGER NOT NULL DEFAULT 1,
                is_active INTEGER NOT NULL DEFAULT 1,
                display_order INTEGER NOT NULL DEFAULT 0,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NULL,
                CHECK (is_required IN (0, 1)),
                CHECK (min_selections >= 0),
                CHECK (max_selections >= min_selections),
                CHECK (is_active IN (0, 1))
            );

            CREATE TABLE menu_modifier_options (
                id TEXT PRIMARY KEY,
                group_id TEXT NOT NULL,
                name TEXT NOT NULL,
                price_delta_cents INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                display_order INTEGER NOT NULL DEFAULT 0,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NULL,
                FOREIGN KEY (group_id) REFERENCES menu_modifier_groups(id),
                CHECK (is_active IN (0, 1)),
                UNIQUE (group_id, name)
            );

            CREATE INDEX idx_menu_modifier_options_group_order
                ON menu_modifier_options(group_id, display_order, name);

            CREATE TABLE menu_item_modifier_groups (
                product_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                display_order INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (product_id, group_id),
                FOREIGN KEY (product_id) REFERENCES products(id),
                FOREIGN KEY (group_id) REFERENCES menu_modifier_groups(id)
            );

            CREATE TABLE menu_variant_modifier_groups (
                variant_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                display_order INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (variant_id, group_id),
                FOREIGN KEY (variant_id) REFERENCES menu_variants(id),
                FOREIGN KEY (group_id) REFERENCES menu_modifier_groups(id)
            );

            CREATE TABLE menu_revisions (
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
                CHECK (status IN ('Published', 'Superseded')),
                UNIQUE (store_id, revision_number),
                UNIQUE (store_id, publication_hash)
            );

            CREATE TABLE menu_revision_categories (
                revision_id TEXT NOT NULL,
                category_id TEXT NOT NULL,
                name TEXT NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, category_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_revision_items (
                revision_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                category_id TEXT NULL,
                name TEXT NOT NULL,
                description TEXT NULL,
                unit_id TEXT NOT NULL,
                unit_name TEXT NULL,
                sku TEXT NULL,
                barcode TEXT NULL,
                selling_price_cents INTEGER NOT NULL,
                cost_price_cents INTEGER NULL,
                is_temporarily_unavailable INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, product_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_revision_variants (
                revision_id TEXT NOT NULL,
                variant_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                name TEXT NOT NULL,
                sku TEXT NULL,
                barcode TEXT NULL,
                selling_price_cents INTEGER NOT NULL,
                cost_price_cents INTEGER NULL,
                is_temporarily_unavailable INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, variant_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_revision_modifier_groups (
                revision_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                name TEXT NOT NULL,
                is_required INTEGER NOT NULL,
                min_selections INTEGER NOT NULL,
                max_selections INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, group_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_revision_modifier_options (
                revision_id TEXT NOT NULL,
                option_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                name TEXT NOT NULL,
                price_delta_cents INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, option_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_revision_item_modifier_groups (
                revision_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, product_id, group_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_revision_variant_modifier_groups (
                revision_id TEXT NOT NULL,
                variant_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                display_order INTEGER NOT NULL,
                PRIMARY KEY (revision_id, variant_id, group_id),
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );

            CREATE TABLE menu_current_publication (
                store_id TEXT PRIMARY KEY,
                revision_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                publication_hash TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL,
                FOREIGN KEY (revision_id) REFERENCES menu_revisions(revision_id)
            );
            """);

        string now = DateTimeOffset.UtcNow.ToString("O");

        using (SqliteCommand availability = connection.CreateCommand())
        {
            availability.Transaction = transaction;
            availability.CommandText = """
                INSERT INTO menu_item_availability (
                    product_id,
                    is_temporarily_unavailable,
                    updated_at_utc)
                SELECT id, 0, $now
                FROM products;
                """;
            availability.Parameters.AddWithValue("$now", now);
            availability.ExecuteNonQuery();
        }

        using (SqliteCommand variants = connection.CreateCommand())
        {
            variants.Transaction = transaction;
            variants.CommandText = """
                INSERT INTO menu_variants (
                    id,
                    product_id,
                    name,
                    sku,
                    barcode,
                    selling_price_cents,
                    cost_price_cents,
                    is_active,
                    is_temporarily_unavailable,
                    display_order,
                    created_at_utc,
                    updated_at_utc)
                SELECT
                    'legacy:' || id,
                    id,
                    'Standard',
                    sku,
                    barcode,
                    selling_price_cents,
                    cost_price_cents,
                    is_active,
                    0,
                    0,
                    $now,
                    NULL
                FROM products;
                """;
            variants.Parameters.AddWithValue("$now", now);
            variants.ExecuteNonQuery();
        }

        CreateImmutableSnapshotTriggers(connection, transaction);
    }

    private static void CreateImmutableSnapshotTriggers(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        string[] tables =
        [
            "menu_revision_categories",
            "menu_revision_items",
            "menu_revision_variants",
            "menu_revision_modifier_groups",
            "menu_revision_modifier_options",
            "menu_revision_item_modifier_groups",
            "menu_revision_variant_modifier_groups"
        ];

        foreach (string table in tables)
        {
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table + "_no_update BEFORE UPDATE ON " + table +
                " BEGIN SELECT RAISE(ABORT, 'published menu snapshot is immutable'); END;");
            Execute(
                connection,
                transaction,
                "CREATE TRIGGER trg_" + table + "_no_delete BEFORE DELETE ON " + table +
                " BEGIN SELECT RAISE(ABORT, 'published menu snapshot is immutable'); END;");
        }

        Execute(connection, transaction, """
            CREATE TRIGGER trg_menu_revisions_immutable_identity
            BEFORE UPDATE ON menu_revisions
            FOR EACH ROW
            WHEN
                NEW.revision_id IS NOT OLD.revision_id OR
                NEW.store_id IS NOT OLD.store_id OR
                NEW.revision_number IS NOT OLD.revision_number OR
                NEW.content_hash IS NOT OLD.content_hash OR
                NEW.publication_hash IS NOT OLD.publication_hash OR
                NEW.created_at_utc IS NOT OLD.created_at_utc OR
                NEW.published_at_utc IS NOT OLD.published_at_utc
            BEGIN
                SELECT RAISE(ABORT, 'published menu revision identity is immutable');
            END;
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_menu_revisions_no_delete
            BEFORE DELETE ON menu_revisions
            BEGIN
                SELECT RAISE(ABORT, 'published menu revision cannot be deleted');
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
