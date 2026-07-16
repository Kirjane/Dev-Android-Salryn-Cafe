using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// PRE-BETA-C: store logo/branding bytes on store_settings (PNG/JPEG/WebP only, validated at API).
/// </summary>
public sealed class StoreLogoBrandingSchemaMigration : IDatabaseMigration
{
    public int Version => 20;
    public string Name => "Store logo branding";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "ALTER TABLE store_settings ADD COLUMN logo_content_type TEXT NULL;";
            command.ExecuteNonQuery();
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "ALTER TABLE store_settings ADD COLUMN logo_bytes BLOB NULL;";
            command.ExecuteNonQuery();
        }
    }
}
