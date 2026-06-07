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

    public void RunPendingMigrations()
    {
        if (_migrations.Count == 0)
        {
            return;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        SqlitePragma.Apply(connection);

        using var transaction = connection.BeginTransaction();

        foreach (var migration in _migrations)
        {
            migration.Up(connection, transaction);
        }

        transaction.Commit();
    }
}
