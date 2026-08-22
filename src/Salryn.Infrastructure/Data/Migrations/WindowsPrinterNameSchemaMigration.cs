using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class WindowsPrinterNameSchemaMigration : IDatabaseMigration
{
    public int Version => 19;
    public string Name => "Windows printer name setting";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE store_settings ADD COLUMN windows_printer_name TEXT NULL;";
        command.ExecuteNonQuery();
    }
}
