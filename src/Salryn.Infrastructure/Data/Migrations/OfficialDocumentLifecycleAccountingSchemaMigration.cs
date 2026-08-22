using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// M4-RC2: line-level refunds, tax reversals, settlements, adjustment documents, real concurrency columns.
/// Preserves v16 tables; expands refund lines and adds operation/settlement/adjustment evidence.
/// </summary>
public sealed class OfficialDocumentLifecycleAccountingSchemaMigration : IDatabaseMigration
{
    public int Version => 17;

    public string Name => "Official document lifecycle accounting (line tax, settlement, adjustment)";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Expand refund lines (new columns; existing rows remain valid).
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN line_index INTEGER NULL;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN disposition TEXT NOT NULL DEFAULT 'Restock';
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN vatable_base_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN vat_amount_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN vat_exempt_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN zero_rated_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN sspt_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN nonvat_exempt_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_refund_lines
            ADD COLUMN operation_id TEXT NULL;
            """);

        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_state
            ADD COLUMN refunded_vatable_base_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_state
            ADD COLUMN refunded_vat_amount_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_state
            ADD COLUMN refunded_vat_exempt_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_state
            ADD COLUMN refunded_zero_rated_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_state
            ADD COLUMN refunded_sspt_cents INTEGER NOT NULL DEFAULT 0;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_state
            ADD COLUMN refunded_nonvat_exempt_cents INTEGER NOT NULL DEFAULT 0;
            """);

        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_idempotency
            ADD COLUMN output_content_sha256 TEXT NULL;
            """);
        Execute(connection, transaction, """
            ALTER TABLE official_document_lifecycle_idempotency
            ADD COLUMN operation_id TEXT NULL;
            """);

        Execute(connection, transaction, """
            CREATE TABLE official_document_lifecycle_operations (
                id TEXT PRIMARY KEY,
                journal_entry_id TEXT NOT NULL,
                action TEXT NOT NULL,
                lifecycle_state_after TEXT NOT NULL,
                reason TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                terminal_id TEXT NULL,
                event_id TEXT NOT NULL,
                adjustment_document_id TEXT NULL,
                settlement_id TEXT NULL,
                tax_reversal_json TEXT NULL,
                created_at TEXT NOT NULL
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE official_document_lifecycle_settlements (
                id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL,
                journal_entry_id TEXT NOT NULL,
                method TEXT NOT NULL,
                amount_cents INTEGER NOT NULL,
                status TEXT NOT NULL,
                external_reference TEXT NULL,
                cashier_session_id TEXT NULL,
                created_at TEXT NOT NULL,
                completed_at TEXT NULL,
                CHECK (amount_cents > 0),
                CHECK (status IN ('Completed','PendingExternal'))
            );
            """);

        Execute(connection, transaction, """
            CREATE TABLE official_document_adjustment_documents (
                id TEXT PRIMARY KEY,
                journal_entry_id TEXT NOT NULL,
                operation_id TEXT NOT NULL,
                original_document_number TEXT NOT NULL,
                internal_document_number TEXT NOT NULL,
                document_kind TEXT NOT NULL,
                legal_series_allocated INTEGER NOT NULL DEFAULT 0,
                snapshot_payload_json TEXT NOT NULL,
                snapshot_payload_hash TEXT NOT NULL,
                snapshot_hash_algorithm TEXT NOT NULL DEFAULT 'SHA-256',
                reason TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                CHECK (legal_series_allocated IN (0, 1)),
                CHECK (length(trim(snapshot_payload_json)) > 0),
                CHECK (length(trim(snapshot_payload_hash)) > 0)
            );
            """);

        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_lifecycle_adjustment_internal_number
            ON official_document_adjustment_documents(internal_document_number);
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_lifecycle_refund_lines_sale_item
            ON official_document_lifecycle_refund_lines(sale_item_id);
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_lifecycle_operations_journal
            ON official_document_lifecycle_operations(journal_entry_id, created_at);
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
