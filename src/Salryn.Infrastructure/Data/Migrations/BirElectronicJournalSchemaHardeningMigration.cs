using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

// Kirch strict dev guide:
// J21-5E owns electronic journal schema hardening only.
// This is a forward-only version 5 migration that protects the version 4 journal tables.
// Do not edit the committed version 4 BirElectronicJournalSchemaMigration.
// Do not add append services, checkout wiring, receipt_snapshot writes, renderer, printer,
// sequence allocation changes, tax calculation, X/Z reading, UI, or official output claims here.
public sealed class BirElectronicJournalSchemaHardeningMigration : IDatabaseMigration
{
    // Kirch strict dev guide:
    // Version 5 must follow version 4 journal schema foundation.
    // Version 5 adds database-level guardrails only; behavior still comes later.
    public int Version => 5;

    public string Name => "BIR electronic journal schema hardening";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Kirch strict dev guide:
        // Journal entries are compliance evidence anchors.
        // UPDATE is blocked so future correction paths must use linked journal events instead of mutation.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entries_no_update
            BEFORE UPDATE ON bir_electronic_journal_entries
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal entries are append-only and cannot be updated.');
            END;
            """);

        // Kirch strict dev guide:
        // DELETE is blocked so journal evidence cannot disappear through normal database writes.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entries_no_delete
            BEFORE DELETE ON bir_electronic_journal_entries
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal entries are append-only and cannot be deleted.');
            END;
            """);

        // Kirch strict dev guide:
        // Journal events are the approved correction trail.
        // UPDATE is blocked so a reversal/reprint/export event cannot be rewritten after creation.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_no_update
            BEFORE UPDATE ON bir_electronic_journal_entry_events
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal entry events are append-only and cannot be updated.');
            END;
            """);

        // Kirch strict dev guide:
        // DELETE is blocked so event history remains reviewable.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_no_delete
            BEFORE DELETE ON bir_electronic_journal_entry_events
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal entry events are append-only and cannot be deleted.');
            END;
            """);

        // Kirch strict dev guide:
        // Preservation batches are C12 evidence anchors.
        // UPDATE is blocked so export/preservation metadata cannot be rewritten after creation.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_preservation_batches_no_update
            BEFORE UPDATE ON bir_electronic_journal_preservation_batches
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal preservation batches are append-only and cannot be updated.');
            END;
            """);

        // Kirch strict dev guide:
        // DELETE is blocked so export/preservation evidence remains visible.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_preservation_batches_no_delete
            BEFORE DELETE ON bir_electronic_journal_preservation_batches
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal preservation batches are append-only and cannot be deleted.');
            END;
            """);

        // Kirch strict dev guide:
        // Reversal events must carry a reason for audit review.
        // This is DB-level hardening only; future service validation must also require the reason.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_reversal_reason_required
            BEFORE INSERT ON bir_electronic_journal_entry_events
            WHEN NEW.event_type IN ('VOID', 'REFUND', 'CANCEL')
             AND (NEW.reason IS NULL OR length(trim(NEW.reason)) = 0)
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal reversal events require a non-blank reason.');
            END;
            """);

        // Kirch strict dev guide:
        // Export, preservation, and backup/recovery events must point to a preservation batch.
        // This protects C12 evidence without adding export behavior.
        Execute(connection, transaction, """
            CREATE TRIGGER trg_bir_electronic_journal_entry_events_preservation_batch_required
            BEFORE INSERT ON bir_electronic_journal_entry_events
            WHEN NEW.event_type IN ('EXPORT', 'PRESERVATION', 'BACKUP_RECOVERY')
             AND (NEW.preservation_batch_id IS NULL OR length(trim(NEW.preservation_batch_id)) = 0)
            BEGIN
                SELECT RAISE(ABORT, 'BIR electronic journal preservation events require a preservation batch.');
            END;
            """);

        // Kirch strict dev guide:
        // Exactly one ORIGINAL event should exist for one journal entry.
        // A partial unique index is simpler and easier to audit than trigger logic for this rule.
        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_bir_electronic_journal_entry_events_single_original
            ON bir_electronic_journal_entry_events(journal_entry_id)
            WHERE event_type = 'ORIGINAL';
            """);

        // Kirch strict dev guide:
        // Snapshot payload/hash fields stay nullable in J21-5E.
        // Renderer and official snapshot persistence do not exist yet.
        // Future append service and later state-aware DB rules must enforce snapshot evidence after that design exists.
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