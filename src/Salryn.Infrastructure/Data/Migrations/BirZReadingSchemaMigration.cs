using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

// Kirch: why - Z-reading records finalize computed official evidence totals without changing original sales, sequence, or journal rows.
public sealed class BirZReadingSchemaMigration : IDatabaseMigration
{
    public int Version => 6;

    public string Name => "BIR Z-reading schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE bir_z_readings (
                id TEXT PRIMARY KEY,
                bir_machine_profile_id TEXT NULL,
                registration_scope_code TEXT NULL,
                document_kind TEXT NOT NULL,
                range_started_at TEXT NOT NULL,
                range_ended_at TEXT NOT NULL,
                finalized_at TEXT NOT NULL,
                entry_count INTEGER NOT NULL,
                first_official_document_number TEXT NULL,
                last_official_document_number TEXT NULL,
                gross_sales_cents INTEGER NOT NULL,
                subtotal_cents INTEGER NOT NULL,
                discount_total_cents INTEGER NOT NULL,
                tax_total_cents INTEGER NOT NULL,
                net_sales_cents INTEGER NOT NULL,
                total_sales_cents INTEGER NOT NULL,
                payment_summary_json TEXT NOT NULL,
                tax_mode_summary_json TEXT NOT NULL,
                evidence_manifest_hash TEXT NOT NULL,
                all_snapshot_hashes_match INTEGER NOT NULL,
                created_by_user_id TEXT NULL,
                terminal_id TEXT NULL,
                status TEXT NOT NULL DEFAULT 'FINALIZED',
                created_at TEXT NOT NULL,
                metadata_json TEXT NULL,

                FOREIGN KEY (bir_machine_profile_id) REFERENCES bir_machine_profiles(id),
                FOREIGN KEY (created_by_user_id) REFERENCES users(id),

                CHECK (
                    bir_machine_profile_id IS NOT NULL
                    OR
                    (registration_scope_code IS NOT NULL AND length(trim(registration_scope_code)) > 0)
                ),
                CHECK (document_kind IN ('OFFICIAL_RECEIPT', 'OFFICIAL_INVOICE')),
                CHECK (length(trim(range_started_at)) > 0),
                CHECK (length(trim(range_ended_at)) > 0),
                CHECK (length(trim(finalized_at)) > 0),
                CHECK (entry_count >= 0),
                CHECK (
                    (first_official_document_number IS NULL AND last_official_document_number IS NULL)
                    OR
                    (first_official_document_number IS NOT NULL AND last_official_document_number IS NOT NULL)
                ),
                CHECK (gross_sales_cents >= 0),
                CHECK (subtotal_cents >= 0),
                CHECK (discount_total_cents >= 0),
                CHECK (tax_total_cents >= 0),
                CHECK (net_sales_cents >= 0),
                CHECK (total_sales_cents >= 0),
                CHECK (length(trim(payment_summary_json)) > 0),
                CHECK (length(trim(tax_mode_summary_json)) > 0),
                CHECK (length(trim(evidence_manifest_hash)) > 0),
                CHECK (all_snapshot_hashes_match IN (0, 1)),
                CHECK (status = 'FINALIZED')
            );
            """);

        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_bir_z_readings_unique_finalized_range
            ON bir_z_readings(
                COALESCE(bir_machine_profile_id, ''),
                COALESCE(registration_scope_code, ''),
                document_kind,
                range_started_at,
                range_ended_at
            )
            WHERE status = 'FINALIZED';
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_bir_z_readings_overlap_lookup
            ON bir_z_readings(
                COALESCE(bir_machine_profile_id, ''),
                COALESCE(registration_scope_code, ''),
                document_kind,
                range_started_at,
                range_ended_at
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_bir_z_readings_finalized_at
            ON bir_z_readings(finalized_at);
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
