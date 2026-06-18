using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

// Kirch strict dev guide:
// J21-2 owns the machine/POS registration schema only.
// This migration exists because RMO-facing receipt, journal, X-reading, Z-reading,
// and accreditation evidence must have a stable machine/taxpayer identity source first.
// Do not add receipt numbering, printing, UI, tenant routing, or hardware adapters here.
public sealed class BirMachineProfileSchemaMigration : IDatabaseMigration
{
    // Kirch strict dev guide:
    // Version 2 is intentionally separate from InitialSchemaMigration.
    // Existing dev databases can migrate forward without rewriting the version 1 foundation.
    public int Version => 2;

    public string Name => "BIR machine profile schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Kirch strict dev guide:
        // bir_machine_profiles is the root compliance identity table.
        // Future official receipt/invoice snapshots should read taxpayer, branch,
        // machine serial/MIN, PTU, accreditation, and software identity from here.
        // Until official_output_enabled is allowed by compliance_mode, output must remain non-official.
        Execute(connection, transaction, """
            CREATE TABLE bir_machine_profiles (
                id TEXT PRIMARY KEY,

                -- Kirch strict dev guide:
                -- Keep single-store/local ownership explicit now.
                -- Future multi-tenant work must replace or extend this ownership boundary carefully.
                registration_scope_code TEXT NOT NULL DEFAULT 'LOCAL_SINGLE_STORE',
                store_settings_id TEXT NULL,

                -- Kirch strict dev guide:
                -- Taxpayer and branch identity belongs here, not inside receipt text only.
                -- Receipt rendering, reports, journals, and exports must reuse these persisted fields.
                registered_taxpayer_name TEXT NOT NULL,
                business_name TEXT NULL,
                registered_address TEXT NOT NULL,
                branch_address TEXT NULL,
                taxpayer_identification_number TEXT NOT NULL,
                branch_code TEXT NOT NULL,

                -- Kirch strict dev guide:
                -- Machine identity must be modeled before official sequence and receipt rendering.
                -- Do not fake this from hostname or random device IDs during compliance work.
                machine_identification_number TEXT NULL,
                machine_serial_number TEXT NOT NULL,
                pos_terminal_number TEXT NULL,

                -- Kirch strict dev guide:
                -- Accreditation/PTU fields are nullable because QA/dev machines may not have approval yet.
                -- official_output_enabled remains blocked until compliance_mode is ACCREDITED.
                accreditation_number TEXT NULL,
                permit_to_use_number TEXT NULL,
                permit_to_use_issued_at TEXT NULL,
                accreditation_issued_at TEXT NULL,
                accreditation_valid_until TEXT NULL,

                -- Kirch strict dev guide:
                -- Software identity must be persisted so accreditation evidence can tie output to a build.
                software_name TEXT NOT NULL DEFAULT 'Salryn',
                software_version TEXT NOT NULL DEFAULT 'UNSET',
                software_build TEXT NULL,

                -- Kirch strict dev guide:
                -- This is the main safety switch.
                -- NOT_ACCREDITED is safe for development and QA.
                -- ACCREDITED is the only mode that may allow official output.
                compliance_mode TEXT NOT NULL DEFAULT 'NOT_ACCREDITED',
                official_output_enabled INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 0,

                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                deactivated_at TEXT NULL,

                FOREIGN KEY (store_settings_id) REFERENCES store_settings(id),

                CHECK (length(trim(registration_scope_code)) > 0),
                CHECK (length(trim(registered_taxpayer_name)) > 0),
                CHECK (length(trim(registered_address)) > 0),
                CHECK (length(trim(taxpayer_identification_number)) > 0),
                CHECK (length(trim(branch_code)) > 0),
                CHECK (length(trim(machine_serial_number)) > 0),
                CHECK (length(trim(software_name)) > 0),
                CHECK (length(trim(software_version)) > 0),
                CHECK (length(trim(compliance_mode)) > 0),
                CHECK (official_output_enabled IN (0, 1)),
                CHECK (is_active IN (0, 1)),

                -- Kirch strict dev guide:
                -- This prevents accidental "official" behavior in dev/QA or before accreditation readiness.
                CHECK ((official_output_enabled = 0) OR (compliance_mode = 'ACCREDITED'))
            );
            """);

        // Kirch strict dev guide:
        // Only one active local scope is allowed for now.
        // Future multi-tenant must change this deliberately instead of accidentally allowing mixed profiles.
        Execute(connection, transaction, """
            CREATE UNIQUE INDEX idx_bir_machine_profiles_active_scope
            ON bir_machine_profiles(registration_scope_code)
            WHERE is_active = 1;
            """);

        // Kirch strict dev guide:
        // Reports and evidence packs will often filter by taxpayer and branch.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_machine_profiles_tin_branch
            ON bir_machine_profiles(taxpayer_identification_number, branch_code);
            """);

        // Kirch strict dev guide:
        // Machine serial lookup supports receipt/journal/report evidence checks later.
        Execute(connection, transaction, """
            CREATE INDEX idx_bir_machine_profiles_serial
            ON bir_machine_profiles(machine_serial_number);
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