using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// M4-RC1: durable lifecycle state + idempotency for official document transitions.
/// Does not mutate historical EJ snapshots.
/// </summary>
public sealed class OfficialDocumentLifecycleCompletionSchemaMigration : IDatabaseMigration
{
    public int Version => 16;

    public string Name => "Official document lifecycle completion state";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE official_document_lifecycle_state (
                journal_entry_id TEXT PRIMARY KEY,
                sale_id TEXT NULL,
                official_document_number TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL,
                original_total_cents INTEGER NOT NULL DEFAULT 0,
                refunded_total_cents INTEGER NOT NULL DEFAULT 0,
                state_version INTEGER NOT NULL DEFAULT 1,
                last_event_id TEXT NULL,
                updated_at TEXT NOT NULL,
                CHECK (lifecycle_state IN (
                    'Committed','PartiallyRefunded','FullyRefunded','Voided','Cancelled')),
                CHECK (original_total_cents >= 0),
                CHECK (refunded_total_cents >= 0),
                CHECK (refunded_total_cents <= original_total_cents),
                CHECK (state_version >= 1)
            );
            """);

        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_official_lifecycle_doc_number
            ON official_document_lifecycle_state(official_document_number);
            """);

        Execute(connection, transaction, """
            CREATE TABLE official_document_lifecycle_idempotency (
                idempotency_key TEXT PRIMARY KEY,
                action TEXT NOT NULL,
                journal_entry_id TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                result_event_id TEXT NOT NULL,
                result_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE official_document_lifecycle_refund_lines (
                id TEXT PRIMARY KEY,
                journal_entry_id TEXT NOT NULL,
                event_id TEXT NOT NULL,
                sale_item_id TEXT NULL,
                quantity_x1000 INTEGER NOT NULL,
                refund_amount_cents INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                CHECK (quantity_x1000 > 0),
                CHECK (refund_amount_cents > 0)
            );
            """);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
