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

    public SchemaLineageClassification ClassifyDatabase()
    {
        if (!File.Exists(_connectionFactory.DatabasePath))
        {
            return new SchemaLineageClassification(
                SchemaLineageKind.Fresh,
                null,
                0,
                "Database file does not exist yet.");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _connectionFactory.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return SchemaLineageClassifier.Classify(connection);
    }

    public int GetPendingMigrationCount()
    {
        if (_migrations.Count == 0)
        {
            return 0;
        }

        if (!File.Exists(_connectionFactory.DatabasePath))
        {
            return _migrations.Count;
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _connectionFactory.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();

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

        bool enforcesCafeLineage = _migrations.Any(m => m is CafeLineageAdoptionAndKernelParityMigration);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        SchemaLineageClassification? lineage = null;
        if (enforcesCafeLineage)
        {
            // Fail closed BEFORE touching schema_migrations or mutating any table.
            lineage = SchemaLineageClassifier.EnsureAllowedForCafeMigration(connection);
        }

        // WAL changes the retained database header. Only configure it after lineage authorization.
        SqlitePragma.Apply(connection);
        EnsureSchemaMigrationsTable(connection);

        var appliedVersions = GetAppliedVersions(connection);

        // A fresh Cafe database must acquire its lineage marker atomically with its
        // foundation. Otherwise a crash before v40 leaves an ambiguous shared lineage
        // that correctly fails closed on the next launch and cannot safely resume.
        if (enforcesCafeLineage && lineage?.Kind == SchemaLineageKind.Fresh)
        {
            using var freshTransaction = connection.BeginTransaction();
            foreach (var migration in _migrations)
            {
                if (appliedVersions.Contains(migration.Version))
                {
                    continue;
                }
                migration.Up(connection, freshTransaction);
                RecordMigration(connection, freshTransaction, migration);
            }
            freshTransaction.Commit();
            return;
        }

        foreach (var migration in _migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            // On legacy Yza v25-v39 DBs, v23 was intentionally skipped in Yza and is adopted/retired atomically inside v40.
            if (lineage?.Kind == SchemaLineageKind.YzaV39Legacy && migration.Version == 23)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();

            migration.Up(connection, transaction);
            RecordMigration(connection, transaction, migration);

            transaction.Commit();
            appliedVersions.Add(migration.Version);

            if (migration is CafeLineageAdoptionAndKernelParityMigration)
            {
                appliedVersions.Add(23);
            }
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
