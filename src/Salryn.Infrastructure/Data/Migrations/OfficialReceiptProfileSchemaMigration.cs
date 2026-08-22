using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// RMO-BIR-M4: receipt/legal text setup only. No sales/checkout changes. No official posting.
/// </summary>
public sealed class OfficialReceiptProfileSchemaMigration : IDatabaseMigration
{
    public int Version => 13;

    public string Name => "Official receipt profile setup schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE official_receipt_profiles (
                id TEXT PRIMARY KEY,
                bir_machine_profile_id TEXT NOT NULL UNIQUE,

                document_title TEXT NOT NULL DEFAULT 'Invoice',
                document_kind TEXT NOT NULL DEFAULT 'OFFICIAL_INVOICE',
                footer_legal_text TEXT NULL,
                supplementary_disclaimer_text TEXT NULL,
                reprint_marker_text TEXT NULL,
                training_marker_text TEXT NULL,
                void_refund_marker_text TEXT NULL,
                invoice_or_transition_mode TEXT NULL,
                sc_pwd_placeholder_enabled INTEGER NOT NULL DEFAULT 0,
                dry_run_only INTEGER NOT NULL DEFAULT 1,

                created_at TEXT NOT NULL,
                updated_at TEXT NULL,
                updated_by_user_id TEXT NULL,

                FOREIGN KEY (bir_machine_profile_id) REFERENCES bir_machine_profiles(id),

                CHECK (length(trim(document_title)) > 0),
                CHECK (document_kind IN ('OFFICIAL_RECEIPT', 'OFFICIAL_INVOICE')),
                CHECK (sc_pwd_placeholder_enabled IN (0, 1)),
                CHECK (dry_run_only IN (0, 1))
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_official_receipt_profiles_machine
            ON official_receipt_profiles(bir_machine_profile_id);
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
