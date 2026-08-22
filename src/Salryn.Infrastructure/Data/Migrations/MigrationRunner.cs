using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public interface IDatabaseMigration
{
    int Version { get; }

    string Name { get; }

    void Up(SqliteConnection connection, SqliteTransaction transaction);
}

public sealed class MigrationRunner
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IReadOnlyList<IDatabaseMigration> _migrations;

    public MigrationRunner(
        SqliteConnectionFactory connectionFactory,
        IEnumerable<IDatabaseMigration> migrations)
    {
        _connectionFactory = connectionFactory;
        _migrations = migrations
            .OrderBy(migration => migration.Version)
            .ToList();
    }

    public IReadOnlyList<IDatabaseMigration> Migrations => _migrations;

    public bool HasMigrations()
    {
        return _migrations.Count > 0;
    }

    public int GetPendingMigrationCount()
    {
        if (_migrations.Count == 0)
        {
            return 0;
        }

        if (!File.Exists(_connectionFactory.DatabasePath))
        {
            return 0;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        SqlitePragma.Apply(connection);

        if (!SchemaMigrationsTableExists(connection))
        {
            return _migrations.Count;
        }

        HashSet<int> appliedVersions = GetAppliedVersions(connection);
        int pendingCount = 0;

        foreach (IDatabaseMigration migration in _migrations)
        {
            if (!appliedVersions.Contains(migration.Version))
            {
                pendingCount++;
            }
        }

        return pendingCount;
    }

    public void RunPendingMigrations()
    {
        if (_migrations.Count == 0)
        {
            return;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        SqlitePragma.Apply(connection);
        EnsureSchemaMigrationsTable(connection);

        var appliedVersions = GetAppliedVersions(connection);

        foreach (var migration in _migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();

            migration.Up(connection, transaction);
            RecordMigration(connection, transaction, migration);

            transaction.Commit();
        }
    }

    private static bool SchemaMigrationsTableExists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = 'schema_migrations'
            LIMIT 1;
            """;

        return command.ExecuteScalar() is not null;
    }

    private static void EnsureSchemaMigrationsTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """;

        command.ExecuteNonQuery();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection connection)
    {
        var versions = new HashSet<int>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations;";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private static void RecordMigration(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IDatabaseMigration migration)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO schema_migrations (version, name, applied_at)
            VALUES ($version, $name, $appliedAt);
            """;

        command.Parameters.AddWithValue("$version", migration.Version);
        command.Parameters.AddWithValue("$name", migration.Name);
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }
}
