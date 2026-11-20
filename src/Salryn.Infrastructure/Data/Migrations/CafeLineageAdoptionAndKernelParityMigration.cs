using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Migration v40 (ADR-I2 / Issue #4):
/// Establishes canonical Salryn.Cafe lineage marker and unconditionally/idempotently
/// ensures Kernel v23 (host_checkout_requests), Kernel v25/v26 (cash_drawer_*),
/// and Kernel v27 (sale void audit reconciliation) exist on BOTH Fresh and YzaV39Legacy DBs.
/// Also retires the skipped v23 slot in schema_migrations on YzaV39Legacy so Pending == 0.
/// </summary>
public sealed class CafeLineageAdoptionAndKernelParityMigration : IDatabaseMigration
{
    public int Version => 40;

    public string Name => "Cafe lineage adoption and Kernel v23/v25/v26/v27 parity";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        // 1. Determine whether this DB came from Fresh (v23 already present) or YzaV39Legacy (v23 skipped).
        bool hadV23BeforeV40 = IsVersionRecorded(connection, transaction, 23);
        int maxPriorVersion = GetMaxRecordedVersion(connection, transaction);
        string lineageCode = hadV23BeforeV40
            ? "SALRYN_CAFE_FRESH_V40"
            : "YZA_V39_LEGACY_ADOPTED_V40";

        // 2. Lineage marker table (id = 1 singleton).
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS salryn_schema_lineage (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                product_id TEXT NOT NULL,
                lineage_code TEXT NOT NULL,
                adopted_from_version INTEGER NOT NULL,
                adopted_at TEXT NOT NULL
            );
            """);

        using (SqliteCommand insertLineage = connection.CreateCommand())
        {
            insertLineage.Transaction = transaction;
            insertLineage.CommandText = """
                INSERT OR IGNORE INTO salryn_schema_lineage (
                    id, product_id, lineage_code, adopted_from_version, adopted_at)
                VALUES (1, $productId, $lineageCode, $adoptedFromVersion, $adoptedAt);
                """;
            insertLineage.Parameters.AddWithValue("$productId", SchemaLineageClassifier.SalrynCafeProductId);
            insertLineage.Parameters.AddWithValue("$lineageCode", lineageCode);
            insertLineage.Parameters.AddWithValue("$adoptedFromVersion", maxPriorVersion);
            insertLineage.Parameters.AddWithValue("$adoptedAt", now);
            insertLineage.ExecuteNonQuery();
        }

        // 3. Unconditional idempotent Kernel v23: host_checkout_requests.
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS host_checkout_requests (
                user_id TEXT NOT NULL,
                client_request_id TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                state TEXT NOT NULL,
                receipt_no TEXT NOT NULL,
                sale_id TEXT NULL,
                total_cents INTEGER NULL,
                change_cents INTEGER NULL,
                message TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (user_id, client_request_id),
                UNIQUE (receipt_no)
            );

            CREATE INDEX IF NOT EXISTS idx_host_checkout_requests_state
                ON host_checkout_requests(state, updated_at_utc);
            """);

        // Retire skipped v23 slot on YzaV39Legacy so MigrationRunner never replays v23 out of order and Pending == 0.
        using (SqliteCommand retireV23 = connection.CreateCommand())
        {
            retireV23.Transaction = transaction;
            retireV23.CommandText = """
                INSERT OR IGNORE INTO schema_migrations (version, name, applied_at)
                VALUES (23, 'HostCheckoutRequestSchemaMigration (adopted-at-v40)', $appliedAt);
                """;
            retireV23.Parameters.AddWithValue("$appliedAt", now);
            retireV23.ExecuteNonQuery();
        }

        // 4. PRAGMA table_info-guarded Kernel v25 column additions.
        if (SchemaLineageClassifier.TableExists(connection, transaction, "cashier_sessions") &&
            !SchemaLineageClassifier.ColumnExists(connection, transaction, "cashier_sessions", "cash_coverage_started_at"))
        {
            Execute(connection, transaction, "ALTER TABLE cashier_sessions ADD COLUMN cash_coverage_started_at TEXT NULL;");
        }

        if (SchemaLineageClassifier.TableExists(connection, transaction, "accounts_receivable_collections") &&
            !SchemaLineageClassifier.ColumnExists(connection, transaction, "accounts_receivable_collections", "cashier_session_id"))
        {
            Execute(connection, transaction, "ALTER TABLE accounts_receivable_collections ADD COLUMN cashier_session_id TEXT NULL REFERENCES cashier_sessions(id);");
        }

        // 5. Unconditional idempotent Kernel v25/v26 cash drawer accountability tables + close_note column.
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS cash_drawer_events (
                id TEXT PRIMARY KEY,
                cashier_session_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                direction INTEGER NOT NULL CHECK(direction IN(-1, 1)),
                amount_cents INTEGER NOT NULL CHECK(amount_cents > 0),
                source_type TEXT NOT NULL,
                source_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                method TEXT NOT NULL,
                occurred_at TEXT NOT NULL,
                request_id TEXT NOT NULL,
                note TEXT NULL,
                FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                UNIQUE(source_type, source_id, event_type),
                UNIQUE(cashier_session_id, request_id)
            );
            CREATE INDEX IF NOT EXISTS idx_cash_drawer_events_session_time
                ON cash_drawer_events(cashier_session_id, occurred_at, id);

            CREATE TABLE IF NOT EXISTS cash_drawer_reconciliations (
                cashier_session_id TEXT PRIMARY KEY,
                expected_cash_cents INTEGER NULL,
                counted_cash_cents INTEGER NOT NULL CHECK(counted_cash_cents >= 0),
                variance_cents INTEGER NULL,
                coverage_status TEXT NOT NULL,
                review_status TEXT NOT NULL,
                close_request_id TEXT NOT NULL UNIQUE,
                submitted_by_user_id TEXT NOT NULL,
                submitted_at TEXT NOT NULL,
                confirmation_text TEXT NOT NULL,
                close_note TEXT NULL,
                FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY(submitted_by_user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS cash_drawer_reviews (
                id TEXT PRIMARY KEY,
                cashier_session_id TEXT NOT NULL,
                reviewer_user_id TEXT NOT NULL,
                decision TEXT NOT NULL,
                reason TEXT NOT NULL,
                request_id TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL,
                FOREIGN KEY(cashier_session_id) REFERENCES cashier_sessions(id),
                FOREIGN KEY(reviewer_user_id) REFERENCES users(id)
            );
            CREATE INDEX IF NOT EXISTS idx_cash_drawer_reviews_session
                ON cash_drawer_reviews(cashier_session_id, created_at, id);
            """);

        if (!SchemaLineageClassifier.ColumnExists(connection, transaction, "cash_drawer_reconciliations", "close_note"))
        {
            Execute(connection, transaction, "ALTER TABLE cash_drawer_reconciliations ADD COLUMN close_note TEXT NULL;");
        }

        // 6. Idempotent Kernel v27 sale void audit backfill.
        BackfillMissingSaleVoidAudits(connection, transaction);
    }

    private static void BackfillMissingSaleVoidAudits(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!SchemaLineageClassifier.TableExists(connection, transaction, "sales") ||
            !SchemaLineageClassifier.TableExists(connection, transaction, "audit_logs"))
        {
            return;
        }

        List<(string SaleId, string? UserId, string ReceiptNo, string? Reason, string VoidedAt)> missing = new();
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT s.id, s.voided_by_user_id, s.receipt_no, s.void_reason, s.voided_at
                FROM sales s
                WHERE s.status = 3
                  AND s.voided_at IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM audit_logs a
                      WHERE a.action = 'SALE_VOIDED'
                        AND a.entity_type = 'Sale'
                        AND a.entity_id = s.id
                  )
                ORDER BY s.voided_at, s.id;
                """;
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                missing.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        foreach (var row in missing)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO audit_logs (
                    id, user_id, action, entity_type, entity_id,
                    old_value_json, new_value_json, reason, created_at)
                VALUES (
                    $id, $userId, 'SALE_VOIDED', 'Sale', $saleId,
                    $oldValue, $newValue, $reason, $createdAt);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$userId", (object?)row.UserId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$saleId", row.SaleId);
            insert.Parameters.AddWithValue("$oldValue", JsonSerializer.Serialize(new { status = "Completed" }));
            insert.Parameters.AddWithValue("$newValue", JsonSerializer.Serialize(new
            {
                saleId = row.SaleId,
                receiptNo = row.ReceiptNo,
                status = "Voided",
                reconciledFromSaleRecord = true
            }));
            insert.Parameters.AddWithValue("$reason", (object?)row.Reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("$createdAt", row.VoidedAt);
            insert.ExecuteNonQuery();
        }
    }

    private static bool IsVersionRecorded(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM schema_migrations WHERE version = $version LIMIT 1;";
        command.Parameters.AddWithValue("$version", version);
        return command.ExecuteScalar() is not null;
    }

    private static int GetMaxRecordedVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        object? result = command.ExecuteScalar();
        return result is long l ? (int)l : 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
