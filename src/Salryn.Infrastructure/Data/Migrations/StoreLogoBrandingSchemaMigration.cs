using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// PRE-BETA-C / PRE-I: store logo/branding bytes on store_settings (PNG/JPEG/WebP only, validated at API).
/// Idempotent: safe if columns already exist (partial upgrade / re-run repair).
/// </summary>
public sealed class StoreLogoBrandingSchemaMigration : IDatabaseMigration
{
    public int Version => 20;
    public string Name => "Store logo branding";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        EnsureColumn(connection, transaction, "logo_content_type", "TEXT NULL");
        EnsureColumn(connection, transaction, "logo_bytes", "BLOB NULL");
    }

    /// <summary>
    /// Repair path for DBs that recorded v20 without columns, or skipped migration at runtime.
    /// Safe to call on every startup after migrations.
    /// </summary>
    public static void EnsureLogoColumnsPresent(SqliteConnection connection)
    {
        if (!TableExists(connection, "store_settings"))
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        EnsureColumn(connection, transaction, "logo_content_type", "TEXT NULL");
        EnsureColumn(connection, transaction, "logo_bytes", "BLOB NULL");
        transaction.Commit();
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string columnSqlType)
    {
        if (ColumnExists(connection, transaction, "store_settings", columnName))
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE store_settings ADD COLUMN {columnName} {columnSqlType};";
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM sqlite_master
            WHERE type = 'table' AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName,
        string columnName)
    {
        using SqliteCommand command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
