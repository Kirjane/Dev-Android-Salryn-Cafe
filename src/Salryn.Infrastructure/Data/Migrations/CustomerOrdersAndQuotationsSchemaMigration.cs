using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CustomerOrdersAndQuotationsSchemaMigration : IDatabaseMigration
{
    public int Version => 30;
    public string Name => "Customer orders and price canvass quotations schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS customer_quotations (
                id TEXT PRIMARY KEY,
                quote_number TEXT NOT NULL UNIQUE,
                customer_name TEXT NOT NULL,
                customer_contact TEXT NULL,
                notes TEXT NULL,
                subtotal_cents INTEGER NOT NULL,
                discount_cents INTEGER NOT NULL DEFAULT 0,
                total_cents INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                created_by_user_id TEXT NOT NULL,
                created_by_user_name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                valid_until TEXT NULL,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS customer_quotation_lines (
                id TEXT PRIMARY KEY,
                quotation_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name_snapshot TEXT NOT NULL,
                barcode_snapshot TEXT NULL,
                unit_name_snapshot TEXT NOT NULL,
                quantity_x1000 INTEGER NOT NULL,
                unit_price_cents INTEGER NOT NULL,
                discount_cents INTEGER NOT NULL DEFAULT 0,
                line_total_cents INTEGER NOT NULL,
                FOREIGN KEY (quotation_id) REFERENCES customer_quotations(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS customer_orders (
                id TEXT PRIMARY KEY,
                order_number TEXT NOT NULL UNIQUE,
                customer_id TEXT NULL,
                customer_name TEXT NOT NULL,
                customer_phone TEXT NULL,
                promised_date TEXT NULL,
                notes TEXT NULL,
                total_cents INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Confirmed',
                sale_id TEXT NULL,
                created_by_user_id TEXT NOT NULL,
                created_by_user_name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                fulfilled_at TEXT NULL,
                FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS customer_order_lines (
                id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name_snapshot TEXT NOT NULL,
                barcode_snapshot TEXT NULL,
                unit_name_snapshot TEXT NOT NULL,
                quantity_x1000 INTEGER NOT NULL,
                unit_price_cents INTEGER NOT NULL,
                line_total_cents INTEGER NOT NULL,
                FOREIGN KEY (order_id) REFERENCES customer_orders(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS idx_customer_quotations_status ON customer_quotations(status);
            CREATE INDEX IF NOT EXISTS idx_customer_orders_status ON customer_orders(status);
            CREATE INDEX IF NOT EXISTS idx_customer_quotation_lines_quote ON customer_quotation_lines(quotation_id);
            CREATE INDEX IF NOT EXISTS idx_customer_order_lines_order ON customer_order_lines(order_id);
            """;
        command.ExecuteNonQuery();
    }
}
