using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CashDrawerCloseReplaySchemaMigration : IDatabaseMigration
{
    public int Version => 26;
    public string Name => "W4 cash drawer close replay payload";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE cash_drawer_reconciliations ADD COLUMN close_note TEXT NULL;
            UPDATE cash_drawer_reconciliations
            SET close_note=(SELECT NULLIF(TRIM(notes),'') FROM cashier_sessions WHERE cashier_sessions.id=cash_drawer_reconciliations.cashier_session_id);
            """;
        command.ExecuteNonQuery();
    }
}
