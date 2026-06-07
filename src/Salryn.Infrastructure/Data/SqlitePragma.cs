using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data;

public static class SqlitePragma
{
    public static void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;

        command.ExecuteNonQuery();
    }
}
