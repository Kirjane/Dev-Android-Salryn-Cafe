using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class StorePrinterModeSchemaMigration : IDatabaseMigration
{
    public int Version => 18;
    public string Name => "Store printer mode";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE store_settings ADD COLUMN printer_mode TEXT NOT NULL DEFAULT 'Disabled';";
        command.ExecuteNonQuery();
    }
}
