using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class ProcureToPayR2HardeningMigration : IDatabaseMigration
{
    public int Version => 29;
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
        // 1. Report discrepancies for historical records that already have inconsistent multi-location totals
        using SqliteCommand checkDiscrepanciesCmd = connection.CreateCommand();
        checkDiscrepanciesCmd.Transaction = transaction;
        checkDiscrepanciesCmd.CommandText = """
            SELECT p.id, p.name, p.stock_on_hand_x1000, COALESCE(SUM(b.quantity_x1000), 0) AS total_bucket_stock
            FROM products p
            LEFT JOIN product_stock_buckets b ON b.product_id = p.id
            WHERE p.track_stock = 1 AND p.is_active = 1
            GROUP BY p.id, p.name, p.stock_on_hand_x1000
            HAVING p.stock_on_hand_x1000 != COALESCE(SUM(b.quantity_x1000), 0)
               AND EXISTS (SELECT 1 FROM product_stock_buckets b2 WHERE b2.product_id = p.id);
            """;
        using (SqliteDataReader reader = checkDiscrepanciesCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string prodId = reader.GetString(0);
                string prodName = reader.GetString(1);
                long productStock = reader.GetInt64(2);
                long bucketStock = reader.GetInt64(3);
                Console.WriteLine($"[Migration v28 WARNING] Inconsistent stock total for product '{prodName}' ({prodId}): product stock_on_hand={productStock / 1000.0:F3}, location buckets total={bucketStock / 1000.0:F3}. Discrepancy preserved without manufacturing fictitious stock.");
            }
        }

        // 2. Only backfill a legacy product when it has NO location buckets anywhere (P1-R3-02)
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
                COALESCE(p.updated_at, p.created_at, datetime('now'))
            FROM products p
            CROSS JOIN inventory_locations loc
            WHERE loc.code = 'MAIN-STORE'
              AND p.track_stock = 1
              AND p.is_active = 1
              AND NOT EXISTS (
                  SELECT 1 FROM product_stock_buckets b WHERE b.product_id = p.id
              );
            """;
        backfillCmd.ExecuteNonQuery();
    }
}
