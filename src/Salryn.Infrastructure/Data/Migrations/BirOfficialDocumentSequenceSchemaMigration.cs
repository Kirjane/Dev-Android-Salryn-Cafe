using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

// Kirch strict dev guide:
// J21-3A owns the official receipt/invoice sequence schema only.
// This migration creates the persistence boundary for RMO-24-2023-C02 sequencing.
// Do not issue numbers, print receipts, generate receipt snapshots, wire checkout,
// resume multi-tenant work, or claim accreditation readiness here.
public sealed class BirOfficialDocumentSequenceSchemaMigration : IDatabaseMigration
{
    // Kirch strict dev guide:
    // Version 3 must follow the version 2 machine profile.
    // Official sequence ownership must be tied to a registered machine/profile boundary first.
    public int Version => 3;

    public string Name => "BIR official document sequence schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Kirch strict dev guide:
        // bir_official_document_sequences is the future source for official receipt/invoice numbering.
        // J21-3A does not allocate numbers yet; it only stores range ownership and guardrails.
        // Runtime allocation must later update current_number transactionally and must not use GUID/random receipt numbers.
        Execute(connection, transaction, """
            CREATE TABLE bir_official_document_sequences (
                id TEXT PRIMARY KEY,

                -- Kirch strict dev guide:
                -- Sequence ownership belongs to a BIR machine profile, not only to store settings or a UI form.
                -- Future tenant/store/branch work must extend this boundary deliberately.
                bir_machine_profile_id TEXT NOT NULL,

                -- Kirch strict dev guide:
                -- Keep receipt and invoice sequences separate without creating separate tables too early.
                document_kind TEXT NOT NULL,

                -- Kirch strict dev guide:
                -- LOCAL_SINGLE_STORE preserves the current MVP ownership model.
                -- Future multi-tenant work must not silently share official sequences across stores or machines.
                sequence_scope_code TEXT NOT NULL DEFAULT 'LOCAL_SINGLE_STORE',

                -- Kirch strict dev guide:
                -- Prefix/year/range fields preserve deterministic sequential numbering.
                -- They are not official output by themselves until a later allocation service gates against machine profile readiness.
                prefix TEXT NOT NULL,
                year INTEGER NOT NULL,
                start_number INTEGER NOT NULL,
                current_number INTEGER NOT NULL,
                end_number INTEGER NULL,
                padding_digits INTEGER NOT NULL DEFAULT 6,

                -- Kirch strict dev guide:
                -- Only one active sequence per owned scope should be used later for allocation.
                -- Exhausted/deactivated rows remain as history and must not be silently reused.
                is_active INTEGER NOT NULL DEFAULT 0,
                is_exhausted INTEGER NOT NULL DEFAULT 0,

                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                deactivated_at TEXT NULL,

                FOREIGN KEY (bir_machine_profile_id) REFERENCES bir_machine_profiles(id),

                CHECK (length(trim(bir_machine_profile_id)) > 0),
                CHECK (document_kind IN ('OFFICIAL_RECEIPT', 'OFFICIAL_INVOICE')),
                CHECK (length(trim(sequence_scope_code)) > 0),
                CHECK (length(trim(prefix)) > 0),
                CHECK (length(trim(prefix)) <= 12),
                CHECK (year >= 2000 AND year <= 9999),
                CHECK (start_number > 0),
                CHECK (current_number >= (start_number - 1)),
                CHECK ((end_number IS NULL) OR (end_number >= start_number)),
                CHECK ((end_number IS NULL) OR (current_number <= end_number)),
                CHECK (padding_digits >= 6 AND padding_digits <= 12),
                CHECK (is_active IN (0, 1)),
                CHECK (is_exhausted IN (0, 1)),
                CHECK ((is_exhausted = 0) OR (is_active = 0))
            );
            """);

        // Kirch strict dev guide:
        // This is the main silent-reuse guard for active official sequence ranges.
        // It prevents two active ranges from competing for the same machine/profile/document/scope/prefix/year.
        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_bir_official_document_sequences_active_scope
            ON bir_official_document_sequences(
                bir_machine_profile_id,
                document_kind,
                sequence_scope_code,
                prefix,
                year
            )
            WHERE is_active = 1;
            """);

        // Kirch strict dev guide:
        // Profile lookup supports future machine-profile gate checks before official number allocation.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_official_document_sequences_profile
            ON bir_official_document_sequences(bir_machine_profile_id);
            """);

        // Kirch strict dev guide:
        // Document/year lookup supports future reports, evidence packs, and range audits.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_official_document_sequences_kind_year
            ON bir_official_document_sequences(document_kind, year);
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