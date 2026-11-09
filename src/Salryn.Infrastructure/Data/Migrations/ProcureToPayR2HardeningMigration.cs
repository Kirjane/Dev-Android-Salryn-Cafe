using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class ProcureToPayR2HardeningMigration : IDatabaseMigration
{
    public int Version => 28;
    public string Name => "LPI procure-to-pay R2 hardening schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS lpi_overdelivery_approvals (
                id TEXT PRIMARY KEY,
                receiving_report_id TEXT NOT NULL,
                purchase_order_id TEXT NOT NULL,
                approver_user_id TEXT NOT NULL,
                approval_reason TEXT NOT NULL,
                approved_lines_json TEXT NOT NULL,
                approved_at TEXT NOT NULL,
                is_revoked INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (receiving_report_id) REFERENCES receiving_reports(id) ON DELETE RESTRICT,
                FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id) ON DELETE RESTRICT,
                FOREIGN KEY (approver_user_id) REFERENCES users(id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS idx_lpi_overdelivery_approvals_rr ON lpi_overdelivery_approvals(receiving_report_id, is_revoked);
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing(connection, transaction, "receiving_reports", "request_payload_hash", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "supplier_payments", "status", "TEXT NOT NULL DEFAULT 'Settled'");
        AddColumnIfMissing(connection, transaction, "supplier_payments", "approved_at", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "supplier_payments", "request_payload_hash", "TEXT NULL");

        BackfillLegacyProductStockBuckets(connection, transaction);
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using SqliteCommand checkCmd = connection.CreateCommand();
        checkCmd.Transaction = transaction;
        checkCmd.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = checkCmd.ExecuteReader();
        while (reader.Read())
        {
            string name = reader.GetString(1);
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        reader.Close();

        using SqliteCommand alterCmd = connection.CreateCommand();
        alterCmd.Transaction = transaction;
        alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCmd.ExecuteNonQuery();
    }

    private static void BackfillLegacyProductStockBuckets(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand backfillCmd = connection.CreateCommand();
        backfillCmd.Transaction = transaction;
        backfillCmd.CommandText = """
            INSERT OR IGNORE INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
            SELECT
                lower(hex(randomblob(16))),
                p.id,
                loc.id,
                'Available',
                p.stock_on_hand_x1000,
                p.updated_at
            FROM products p
            CROSS JOIN inventory_locations loc
            WHERE loc.code = 'MAIN-STORE'
              AND p.track_stock = 1
              AND p.is_active = 1;
            """;
        backfillCmd.ExecuteNonQuery();
    }
}
