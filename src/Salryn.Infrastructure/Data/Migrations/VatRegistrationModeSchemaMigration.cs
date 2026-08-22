using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// TRAD-M1: persist installation taxpayer VAT / non-VAT registration mode on machine profile.
/// Nullable column — no invented default mode for existing installs. No live official posting.
/// </summary>
public sealed class VatRegistrationModeSchemaMigration : IDatabaseMigration
{
    public int Version => 15;

    public string Name => "VAT registration mode on BIR machine profile";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            ALTER TABLE bir_machine_profiles
            ADD COLUMN vat_registration_mode TEXT NULL;
            """);

        // Index supports readiness / reporting filters by mode without inventing defaults.
        Execute(connection, transaction, """
            CREATE INDEX IF NOT EXISTS idx_bir_machine_profiles_vat_registration_mode
            ON bir_machine_profiles(vat_registration_mode);
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
