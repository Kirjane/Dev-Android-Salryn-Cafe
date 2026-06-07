using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class InitialSchemaMigration : IDatabaseMigration
{
    public int Version => 1;

    public string Name => "Initial Salryn schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE app_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE roles (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                is_system_role INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE users (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                role_id TEXT NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                FOREIGN KEY (role_id) REFERENCES roles(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE role_permissions (
                id TEXT PRIMARY KEY,
                role_id TEXT NOT NULL,
                permission TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (role_id) REFERENCES roles(id),
                UNIQUE (role_id, permission)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE store_settings (
                id TEXT PRIMARY KEY,
                store_name TEXT NOT NULL,
                business_name TEXT NULL,
                address TEXT NULL,
                contact_number TEXT NULL,
                tin TEXT NULL,
                currency_label TEXT NOT NULL,
                receipt_width_mm INTEGER NOT NULL,
                receipt_header TEXT NULL,
                receipt_footer TEXT NULL,
                tax_mode_enabled INTEGER NOT NULL DEFAULT 0,
                shift_mode_enabled INTEGER NOT NULL DEFAULT 1,
                allow_negative_stock INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE categories (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE brands (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE units (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                abbreviation TEXT NULL,
                allows_decimal_quantity INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE suppliers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                contact_number TEXT NULL,
                notes TEXT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE products (
                id TEXT PRIMARY KEY,
                sku TEXT NULL,
                barcode TEXT NULL,
                name TEXT NOT NULL,
                description TEXT NULL,
                category_id TEXT NULL,
                brand_id TEXT NULL,
                unit_id TEXT NOT NULL,
                supplier_id TEXT NULL,
                cost_price_cents INTEGER NULL,
                selling_price_cents INTEGER NOT NULL,
                wholesale_price_cents INTEGER NULL,
                stock_on_hand_x1000 INTEGER NOT NULL DEFAULT 0,
                reorder_level_x1000 INTEGER NOT NULL DEFAULT 0,
                track_stock INTEGER NOT NULL DEFAULT 1,
                allow_discount INTEGER NOT NULL DEFAULT 1,
                is_active INTEGER NOT NULL DEFAULT 1,
                notes TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                FOREIGN KEY (category_id) REFERENCES categories(id),
                FOREIGN KEY (brand_id) REFERENCES brands(id),
                FOREIGN KEY (unit_id) REFERENCES units(id),
                FOREIGN KEY (supplier_id) REFERENCES suppliers(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_products_barcode_active
            ON products(barcode)
            WHERE barcode IS NOT NULL AND is_active = 1;
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_products_name ON products(name);
            """);
        Execute(connection, transaction, """
            CREATE TABLE receipt_sequences (
                id TEXT PRIMARY KEY,
                prefix TEXT NOT NULL,
                year INTEGER NOT NULL,
                current_number INTEGER NOT NULL DEFAULT 0,
                padding_digits INTEGER NOT NULL DEFAULT 6,
                updated_at TEXT NOT NULL,
                UNIQUE (prefix, year)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE cashier_sessions (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                opened_at TEXT NOT NULL,
                closed_at TEXT NULL,
                opening_cash_cents INTEGER NOT NULL DEFAULT 0,
                expected_cash_cents INTEGER NOT NULL DEFAULT 0,
                counted_cash_cents INTEGER NULL,
                variance_cents INTEGER NOT NULL DEFAULT 0,
                status INTEGER NOT NULL,
                notes TEXT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_cashier_sessions_user_status
            ON cashier_sessions(user_id, status);
            """);

        Execute(connection, transaction, """
            CREATE TABLE sales (
                id TEXT PRIMARY KEY,
                receipt_no TEXT NOT NULL UNIQUE,
                sale_date TEXT NOT NULL,
                cashier_user_id TEXT NOT NULL,
                cashier_session_id TEXT NULL,
                subtotal_cents INTEGER NOT NULL,
                discount_cents INTEGER NOT NULL DEFAULT 0,
                tax_cents INTEGER NOT NULL DEFAULT 0,
                total_cents INTEGER NOT NULL,
                amount_paid_cents INTEGER NOT NULL,
                change_cents INTEGER NOT NULL,
                status INTEGER NOT NULL,
                voided_by_user_id TEXT NULL,
                void_reason TEXT NULL,
                voided_at TEXT NULL,
                receipt_snapshot TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (cashier_user_id) REFERENCES users(id),
                FOREIGN KEY (cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY (voided_by_user_id) REFERENCES users(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_sales_sale_date ON sales(sale_date);
            """);

        Execute(connection, transaction, """
            CREATE TABLE sale_items (
                id TEXT PRIMARY KEY,
                sale_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name_snapshot TEXT NOT NULL,
                barcode_snapshot TEXT NULL,
                unit_name_snapshot TEXT NOT NULL,
                qty_x1000 INTEGER NOT NULL,
                unit_price_cents INTEGER NOT NULL,
                cost_price_cents INTEGER NULL,
                discount_cents INTEGER NOT NULL DEFAULT 0,
                line_total_cents INTEGER NOT NULL,
                FOREIGN KEY (sale_id) REFERENCES sales(id),
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_sale_items_sale_id ON sale_items(sale_id);
            """);

        Execute(connection, transaction, """
            CREATE TABLE payments (
                id TEXT PRIMARY KEY,
                sale_id TEXT NOT NULL,
                method INTEGER NOT NULL,
                amount_cents INTEGER NOT NULL,
                paid_at TEXT NOT NULL,
                FOREIGN KEY (sale_id) REFERENCES sales(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE stock_movements (
                id TEXT PRIMARY KEY,
                product_id TEXT NOT NULL,
                movement_type INTEGER NOT NULL,
                qty_change_x1000 INTEGER NOT NULL,
                cost_price_cents INTEGER NULL,
                reference_type TEXT NULL,
                reference_id TEXT NULL,
                reason TEXT NULL,
                user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id),
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_stock_movements_product_created
            ON stock_movements(product_id, created_at);
            """);
        Execute(connection, transaction, """
            CREATE TABLE custom_field_definitions (
                id TEXT PRIMARY KEY,
                entity_type TEXT NOT NULL,
                field_name TEXT NOT NULL,
                field_label TEXT NOT NULL,
                data_type TEXT NOT NULL,
                is_required INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                UNIQUE (entity_type, field_name)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE custom_field_values (
                id TEXT PRIMARY KEY,
                definition_id TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                value TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                FOREIGN KEY (definition_id) REFERENCES custom_field_definitions(id),
                UNIQUE (definition_id, entity_id)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE print_jobs (
                id TEXT PRIMARY KEY,
                job_type TEXT NOT NULL,
                reference_type TEXT NOT NULL,
                reference_id TEXT NOT NULL,
                printer_name TEXT NULL,
                payload_text TEXT NOT NULL,
                status INTEGER NOT NULL,
                error_message TEXT NULL,
                created_at TEXT NOT NULL,
                printed_at TEXT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE audit_logs (
                id TEXT PRIMARY KEY,
                user_id TEXT NULL,
                action TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NULL,
                old_value_json TEXT NULL,
                new_value_json TEXT NULL,
                reason TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE error_logs (
                id TEXT PRIMARY KEY,
                user_id TEXT NULL,
                area TEXT NOT NULL,
                message TEXT NOT NULL,
                technical_detail TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE import_export_jobs (
                id TEXT PRIMARY KEY,
                job_type INTEGER NOT NULL,
                file_path TEXT NOT NULL,
                status INTEGER NOT NULL,
                total_rows INTEGER NOT NULL DEFAULT 0,
                success_rows INTEGER NOT NULL DEFAULT 0,
                failed_rows INTEGER NOT NULL DEFAULT 0,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                error_summary TEXT NULL,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id)
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE backup_logs (
                id TEXT PRIMARY KEY,
                file_path TEXT NOT NULL,
                status INTEGER NOT NULL,
                file_size_bytes INTEGER NULL,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                error_message TEXT NULL,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id)
            );
            """);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
