using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

// Kirch: why - CORRECTION must be an append-only journal event, so this widens the existing event table instead of adding a parallel audit table.
public sealed class BirElectronicJournalCorrectionEventSchemaMigration : IDatabaseMigration
{
    public int Version => 7;

    public string Name => "BIR electronic journal correction event schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        DropEventGuards(connection, transaction);

        Execute(connection, transaction, """
            ALTER TABLE bir_electronic_journal_entry_events
            RENAME TO bir_electronic_journal_entry_events_v6;
            """);

        CreateEventTable(connection, transaction);

        Execute(connection, transaction, """
            INSERT INTO bir_electronic_journal_entry_events (
                id,
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                reason,
                preservation_batch_id,
                created_at,
                metadata_json
            )
            SELECT
                id,
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                reason,
                preservation_batch_id,
                created_at,
                metadata_json
            FROM bir_electronic_journal_entry_events_v6;
            """);

        Execute(connection, transaction, """
            DROP TABLE bir_electronic_journal_entry_events_v6;
            """);

        CreateEventIndexesAndGuards(connection, transaction);
    }

    private static void DropEventGuards(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, "DROP TRIGGER IF EXISTS trg_bir_electronic_journal_entry_events_no_update;");
        Execute(connection, transaction, "DROP TRIGGER IF EXISTS trg_bir_electronic_journal_entry_events_no_delete;");
        Execute(connection, transaction, "DROP TRIGGER IF EXISTS trg_bir_electronic_journal_entry_events_reversal_reason_required;");
        Execute(connection, transaction, "DROP TRIGGER IF EXISTS trg_bir_electronic_journal_entry_events_preservation_batch_required;");
        Execute(connection, transaction, "DROP INDEX IF EXISTS idx_bir_electronic_journal_entry_events_single_original;");
        Execute(connection, transaction, "DROP INDEX IF EXISTS idx_bir_electronic_journal_entry_events_entry;");
        Execute(connection, transaction, "DROP INDEX IF EXISTS idx_bir_electronic_journal_entry_events_type_created;");
        Execute(connection, transaction, "DROP INDEX IF EXISTS idx_bir_electronic_journal_entry_events_cashier;");
    }

    private static void CreateEventTable(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
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
                CHECK (event_type IN ('ORIGINAL', 'REPRINT', 'VOID', 'REFUND', 'CANCEL', 'CORRECTION', 'EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY')),
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
                    (event_type = 'CORRECTION' AND original_marker = 0 AND reprint_marker = 0 AND void_marker = 0 AND refund_marker = 0 AND cancel_marker = 0)
                    OR
                    (event_type IN ('EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY') AND original_marker = 0 AND reprint_marker = 0 AND void_marker = 0 AND refund_marker = 0 AND cancel_marker = 0)
                )
            );
            """);
    }

    private static void CreateEventIndexesAndGuards(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entry_events_entry
            ON bir_electronic_journal_entry_events(journal_entry_id);
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entry_events_type_created
            ON bir_electronic_journal_entry_events(event_type, created_at);
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_bir_electronic_journal_entry_events_cashier
            ON bir_electronic_journal_entry_events(cashier_user_id);
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_no_update
            BEFORE UPDATE ON bir_electronic_journal_entry_events
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal entry events are append-only and cannot be updated.');
            END;
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_no_delete
            BEFORE DELETE ON bir_electronic_journal_entry_events
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal entry events are append-only and cannot be deleted.');
            END;
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_reversal_reason_required
            BEFORE INSERT ON bir_electronic_journal_entry_events
            WHEN NEW.event_type IN ('VOID', 'REFUND', 'CANCEL', 'CORRECTION')
             AND (NEW.reason IS NULL OR length(trim(NEW.reason)) = 0)
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal audit events require a non-blank reason.');
            END;
            """);

        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_preservation_batch_required
            BEFORE INSERT ON bir_electronic_journal_entry_events
            WHEN NEW.event_type IN ('EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY')
             AND (NEW.preservation_batch_id IS NULL OR length(trim(NEW.preservation_batch_id)) = 0)
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal preservation events require a preservation batch.');
            END;
            """);

        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_bir_electronic_journal_entry_events_single_original
            ON bir_electronic_journal_entry_events(journal_entry_id)
            WHERE event_type = 'ORIGINAL';
            """);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
