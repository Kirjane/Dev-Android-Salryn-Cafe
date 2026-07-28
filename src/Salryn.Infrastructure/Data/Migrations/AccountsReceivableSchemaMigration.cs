using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class AccountsReceivableSchemaMigration : IDatabaseMigration
{
    public int Version => 22;
    public string Name => "U1 accounts receivable ledger";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE customers (
                id TEXT PRIMARY KEY,
                full_name TEXT NOT NULL,
                phone TEXT NULL,
                address TEXT NULL,
                note TEXT NULL,
                credit_limit_cents INTEGER NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (created_by_user_id) REFERENCES users(id)
            );

            CREATE INDEX idx_customers_name ON customers(full_name);
            CREATE INDEX idx_customers_phone ON customers(phone);

            CREATE TABLE accounts_receivable_collections (
                id TEXT PRIMARY KEY,
                customer_id TEXT NOT NULL,
                amount_cents INTEGER NOT NULL CHECK (amount_cents > 0),
                method TEXT NOT NULL,
                note TEXT NULL,
                collected_by_user_id TEXT NOT NULL,
                collected_at TEXT NOT NULL,
                voided_at TEXT NULL,
                voided_by_user_id TEXT NULL,
                void_reason TEXT NULL,
                FOREIGN KEY (customer_id) REFERENCES customers(id),
                FOREIGN KEY (collected_by_user_id) REFERENCES users(id),
                FOREIGN KEY (voided_by_user_id) REFERENCES users(id)
            );

            CREATE TABLE accounts_receivable_entries (
                id TEXT PRIMARY KEY,
                customer_id TEXT NOT NULL,
                sale_id TEXT NULL,
                collection_id TEXT NULL,
                entry_type TEXT NOT NULL,
                debit_cents INTEGER NOT NULL DEFAULT 0 CHECK (debit_cents >= 0),
                credit_cents INTEGER NOT NULL DEFAULT 0 CHECK (credit_cents >= 0),
                balance_after_cents INTEGER NOT NULL CHECK (balance_after_cents >= 0),
                due_date TEXT NULL,
                note TEXT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (customer_id) REFERENCES customers(id),
                FOREIGN KEY (sale_id) REFERENCES sales(id),
                FOREIGN KEY (collection_id) REFERENCES accounts_receivable_collections(id),
                FOREIGN KEY (created_by_user_id) REFERENCES users(id),
                CHECK ((debit_cents > 0 AND credit_cents = 0) OR
                       (credit_cents > 0 AND debit_cents = 0))
            );

            CREATE INDEX idx_ar_entries_customer_created
                ON accounts_receivable_entries(customer_id, created_at, id);
            CREATE UNIQUE INDEX idx_ar_entries_sale_debit
                ON accounts_receivable_entries(sale_id)
                WHERE sale_id IS NOT NULL AND entry_type = 'CreditSale';
            CREATE UNIQUE INDEX idx_ar_entries_collection
                ON accounts_receivable_entries(collection_id, entry_type)
                WHERE collection_id IS NOT NULL;

            ALTER TABLE sales ADD COLUMN customer_id TEXT NULL REFERENCES customers(id);
            ALTER TABLE sales ADD COLUMN payment_status TEXT NOT NULL DEFAULT 'Paid';
            ALTER TABLE sales ADD COLUMN balance_due_cents INTEGER NOT NULL DEFAULT 0
                CHECK (balance_due_cents >= 0);
            ALTER TABLE sales ADD COLUMN receivable_due_date TEXT NULL;
            ALTER TABLE sales ADD COLUMN receivable_note TEXT NULL;

            CREATE INDEX idx_sales_customer_id ON sales(customer_id);
            """;
        command.ExecuteNonQuery();
    }
}
