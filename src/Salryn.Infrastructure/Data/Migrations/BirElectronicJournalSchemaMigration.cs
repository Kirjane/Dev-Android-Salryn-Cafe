using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

// Kirch strict dev guide:
// J21-5C owns the electronic journal schema foundation only.
// This migration creates future storage boundaries for RMO-24-2023-C08 journal evidence,
// partial C07 append-only/event integrity evidence, and partial C12 preservation evidence.
// Do not wire checkout, write sale receipt_snapshot, persist official documents from live sales,
// render output, print output, allocate sequences, calculate tax, create X/Z readings, add UI,
// or claim official receipt/invoice readiness here.
public sealed class BirElectronicJournalSchemaMigration : IDatabaseMigration
{
    // Kirch strict dev guide:
    // Version 4 must follow the version 3 official document sequence schema.
    // Journal rows need machine profile, sale, user, and official sequence references to exist first.
    public int Version => 4;

    public string Name => "BIR electronic journal schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Kirch strict dev guide:
        // bir_electronic_journal_entries is the future C08 journal anchor table.
        // It stores official-document identity and snapshot hash fields for a later approved append service.
        // This schema does not create the append service and must not be called from checkout yet.
        Execute(connection, transaction, """
            CREATE TABLE bir_electronic_journal_entries (
                id TEXT PRIMARY KEY,

                -- Kirch strict dev guide:
                -- Machine profile ownership keeps journal evidence tied to taxpayer, branch, serial/MIN, PTU, and accreditation context.
                bir_machine_profile_id TEXT NOT NULL,

                -- Kirch strict dev guide:
                -- The sale reference is future evidence ownership only.
                -- J21-5C does not write journal rows from the sale checkout flow.
                sale_id TEXT NOT NULL,

                -- Kirch strict dev guide:
                -- Sequence identity stays separate from allocation.
                -- A later atomic design must confirm how allocation, snapshot persistence, and journal append happen together.
                official_document_sequence_id TEXT NOT NULL,

                official_document_number TEXT NOT NULL,
                document_kind TEXT NOT NULL,
                transaction_timestamp TEXT NOT NULL,
                cashier_user_id TEXT NOT NULL,
                terminal_id TEXT NULL,

                -- Kirch strict dev guide:
                -- Snapshot payload/hash fields are nullable because no renderer or snapshot persistence exists in J21-5C.
                -- A later approved renderer/persistence phase must decide payload format and hashing rules.
                snapshot_payload_json TEXT NULL,
                snapshot_payload_hash TEXT NULL,
                snapshot_hash_algorithm TEXT NULL,

                created_at TEXT NOT NULL,
                metadata_json TEXT NULL,

                FOREIGN KEY (bir_machine_profile_id) REFERENCES bir_machine_profiles(id),
                FOREIGN KEY (sale_id) REFERENCES sales(id),
                FOREIGN KEY (official_document_sequence_id) REFERENCES bir_official_document_sequences(id),
                FOREIGN KEY (cashier_user_id) REFERENCES users(id),

                CHECK (length(trim(bir_machine_profile_id)) > 0),
                CHECK (length(trim(sale_id)) > 0),
                CHECK (length(trim(official_document_sequence_id)) > 0),
                CHECK (length(trim(official_document_number)) > 0),
                CHECK (document_kind IN ('OFFICIAL_RECEIPT', 'OFFICIAL_INVOICE')),
                CHECK (length(trim(transaction_timestamp)) > 0),
                CHECK (length(trim(cashier_user_id)) > 0),
                CHECK (
                    (snapshot_payload_hash IS NULL AND snapshot_hash_algorithm IS NULL)
                    OR
                    (snapshot_payload_hash IS NOT NULL AND snapshot_hash_algorithm IS NOT NULL)
                )
            );
            """);

        // Kirch strict dev guide:
        // This prevents duplicate official document journal anchors for the same machine/profile/document identity.
        // It is schema integrity only, not a live append path.
        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_bir_electronic_journal_entries_doc_identity
            ON bir_electronic_journal_entries(
                bir_machine_profile_id,
                document_kind,
                official_document_number
            );
            """);

        // Kirch strict dev guide:
        // Machine/time lookup supports future journal review, export, and retention queries.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entries_machine_time
            ON bir_electronic_journal_entries(bir_machine_profile_id, transaction_timestamp);
            """);

        // Kirch strict dev guide:
        // Sale lookup preserves the boundary between MVP sales and future RMO journal evidence.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entries_sale
            ON bir_electronic_journal_entries(sale_id);
            """);

        // Kirch strict dev guide:
        // Cashier lookup supports future accountability and audit review.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entries_cashier
            ON bir_electronic_journal_entries(cashier_user_id);
            """);

        // Kirch strict dev guide:
        // bir_electronic_journal_preservation_batches is future C12 preservation/export evidence storage.
        // It does not export files, mutate journal entries, or implement backup behavior in J21-5C.
        Execute(connection, transaction, """
            CREATE TABLE bir_electronic_journal_preservation_batches (
                id TEXT PRIMARY KEY,
                batch_type TEXT NOT NULL,
                range_started_at TEXT NULL,
                range_ended_at TEXT NULL,
                journal_entry_count INTEGER NOT NULL DEFAULT 0,
                storage_reference TEXT NULL,
                file_hash TEXT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                metadata_json TEXT NULL,

                FOREIGN KEY (created_by_user_id) REFERENCES users(id),

                CHECK (batch_type IN ('EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY')),
                CHECK (journal_entry_count >= 0),
                CHECK (length(trim(created_by_user_id)) > 0)
            );
            """);

        // Kirch strict dev guide:
        // Batch type/time lookup supports future retention and preservation review.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_preservation_batches_type_created
            ON bir_electronic_journal_preservation_batches(batch_type, created_at);
            """);

        // Kirch strict dev guide:
        // bir_electronic_journal_entry_events stores future linked events instead of editing the original entry.
        // This is the schema foundation for original/reprint/void/refund/cancel/export/preservation markers only.
        // No append service, checkout integration, renderer, printer, or official output behavior belongs here.
        Execute(connection, transaction, """
            CREATE TABLE bir_electronic_journal_entry_events (
                id TEXT PRIMARY KEY,
                journal_entry_id TEXT NOT NULL,
                event_type TEXT NOT NULL,

                original_marker INTEGER NOT NULL DEFAULT 0,
                reprint_marker INTEGER NOT NULL DEFAULT 0,
                void_marker INTEGER NOT NULL DEFAULT 0,
                refund_marker INTEGER NOT NULL DEFAULT 0,
                cancel_marker INTEGER NOT NULL DEFAULT 0,

                cashier_user_id TEXT NOT NULL,
                reason TEXT NULL,
                preservation_batch_id TEXT NULL,
                created_at TEXT NOT NULL,
                metadata_json TEXT NULL,

                FOREIGN KEY (journal_entry_id) REFERENCES bir_electronic_journal_entries(id),
                FOREIGN KEY (cashier_user_id) REFERENCES users(id),
                FOREIGN KEY (preservation_batch_id) REFERENCES bir_electronic_journal_preservation_batches(id),

                CHECK (length(trim(journal_entry_id)) > 0),
                CHECK (event_type IN ('ORIGINAL', 'REPRINT', 'VOID', 'REFUND', 'CANCEL', 'EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY')),
                CHECK (original_marker IN (0, 1)),
                CHECK (reprint_marker IN (0, 1)),
                CHECK (void_marker IN (0, 1)),
                CHECK (refund_marker IN (0, 1)),
                CHECK (cancel_marker IN (0, 1)),
                CHECK (
                    (event_type = 'ORIGINAL' AND original_marker = 1 AND reprint_marker = 0 AND void_marker = 0 AND refund_marker = 0 AND cancel_marker = 0)
                    OR
                    (event_type = 'REPRINT' AND original_marker = 0 AND reprint_marker = 1 AND void_marker = 0 AND refund_marker = 0 AND cancel_marker = 0)
                    OR
                    (event_type = 'VOID' AND original_marker = 0 AND reprint_marker = 0 AND void_marker = 1 AND refund_marker = 0 AND cancel_marker = 0)
                    OR
                    (event_type = 'REFUND' AND original_marker = 0 AND reprint_marker = 0 AND void_marker = 0 AND refund_marker = 1 AND cancel_marker = 0)
                    OR
                    (event_type = 'CANCEL' AND original_marker = 0 AND reprint_marker = 0 AND void_marker = 0 AND refund_marker = 0 AND cancel_marker = 1)
                    OR
                    (event_type IN ('EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY') AND original_marker = 0 AND reprint_marker = 0 AND void_marker = 0 AND refund_marker = 0 AND cancel_marker = 0)
                )
            );
            """);

        // Kirch strict dev guide:
        // Entry lookup supports future event review without editing the journal anchor.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entry_events_entry
            ON bir_electronic_journal_entry_events(journal_entry_id);
            """);

        // Kirch strict dev guide:
        // Event/time lookup supports future reprint, void, refund, cancel, export, and preservation reports.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entry_events_type_created
            ON bir_electronic_journal_entry_events(event_type, created_at);
            """);

        // Kirch strict dev guide:
        // Cashier lookup supports future accountability and review.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entry_events_cashier
            ON bir_electronic_journal_entry_events(cashier_user_id);
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