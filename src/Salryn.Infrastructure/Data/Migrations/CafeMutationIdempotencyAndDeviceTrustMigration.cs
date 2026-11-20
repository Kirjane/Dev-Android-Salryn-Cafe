using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Migration v41 (ADR-I2 / ADR-I4 / ADR-I6 — Issues #4, #5, #6):
/// 1. Unified append-only C1-C11 durable idempotency ledger (`cafe_mutation_idempotency`)
///    with immutability triggers (`no_update`, `no_delete`).
/// 2. Device trust, asymmetric key binding, offline authorization lease, and anti-rollback
///    columns/tables for `Salryn.Cafe` terminals and offline Android cashiers.
/// </summary>
public sealed class CafeMutationIdempotencyAndDeviceTrustMigration : IDatabaseMigration
{
    public int Version => 41;

    public string Name => "Cafe unified mutation idempotency ledger and device trust schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // 1. Unified C1-C11 durable mutation idempotency ledger (locked #79/#81/#82).
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS cafe_mutation_idempotency (
                operation_id TEXT PRIMARY KEY,
                endpoint_key TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                status_code INTEGER NOT NULL,
                response_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_cafe_mutation_idempotency_endpoint_created
                ON cafe_mutation_idempotency (endpoint_key, created_at_utc);

            CREATE TRIGGER IF NOT EXISTS trg_cafe_mutation_idempotency_no_update
                BEFORE UPDATE ON cafe_mutation_idempotency
                BEGIN
                    SELECT RAISE(ABORT, 'cafe_mutation_idempotency rows are immutable and cannot be updated.');
                END;

            CREATE TRIGGER IF NOT EXISTS trg_cafe_mutation_idempotency_no_delete
                BEFORE DELETE ON cafe_mutation_idempotency
                BEGIN
                    SELECT RAISE(ABORT, 'cafe_mutation_idempotency rows are immutable and cannot be deleted.');
                END;
            """);

        // 2. Device trust / asymmetric key / product-scope columns on terminal registry & invites.
        if (SchemaLineageClassifier.TableExists(connection, transaction, "salryn_terminal_registrations"))
        {
            AddColumnIfMissing(connection, transaction, "salryn_terminal_registrations", "product_id", "TEXT NOT NULL DEFAULT 'Salryn.Cafe'");
            AddColumnIfMissing(connection, transaction, "salryn_terminal_registrations", "device_role", "TEXT NOT NULL DEFAULT 'LanThinTerminal'");
            AddColumnIfMissing(connection, transaction, "salryn_terminal_registrations", "public_key_thumbprint", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "salryn_terminal_registrations", "public_key_pem", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "salryn_terminal_registrations", "offline_lease_expires_at_utc", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "salryn_terminal_registrations", "sync_epoch", "INTEGER NOT NULL DEFAULT 1");
        }

        if (SchemaLineageClassifier.TableExists(connection, transaction, "salryn_terminal_pairing_invites"))
        {
            AddColumnIfMissing(connection, transaction, "salryn_terminal_pairing_invites", "product_id", "TEXT NOT NULL DEFAULT 'Salryn.Cafe'");
            AddColumnIfMissing(connection, transaction, "salryn_terminal_pairing_invites", "target_device_role", "TEXT NOT NULL DEFAULT 'LanThinTerminal'");
        }

        // 3. Local device trust, offline lease & anti-rollback high-water mark table (ADR-I4/I6).
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS sync_device_trust_state (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                product_id TEXT NOT NULL DEFAULT 'Salryn.Cafe',
                tenant_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                device_role TEXT NOT NULL,
                public_key_thumbprint TEXT NOT NULL,
                sync_epoch INTEGER NOT NULL DEFAULT 1,
                lease_expires_at_utc TEXT NOT NULL,
                last_attested_utc TEXT NOT NULL,
                last_observed_utc TEXT NOT NULL,
                last_seen_monotonic_ms INTEGER NOT NULL DEFAULT 0,
                last_boot_count INTEGER NOT NULL DEFAULT 0,
                revoked_at_utc TEXT NULL,
                high_water_mac TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """);
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        if (!SchemaLineageClassifier.ColumnExists(connection, transaction, tableName, columnName))
        {
            Execute(connection, transaction, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
