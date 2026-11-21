using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Paths;

namespace Salryn.Infrastructure.Data;

public sealed class SqliteConnectionFactory
{
    private readonly DbPaths? _dbPaths;
    private readonly string _databasePath;

    public SqliteConnectionFactory(DbPaths dbPaths)
    {
        _dbPaths = dbPaths ?? throw new ArgumentNullException(nameof(dbPaths));
        _databasePath = dbPaths.DatabasePath;
    }

    /// <summary>Explicit host-configured file for canonical persistence, including Central.
    /// Callers must still run the fail-closed Cafe lineage classifier before migration.</summary>
    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public string CreateConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        return builder.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        if (_dbPaths is not null) _dbPaths.EnsureDirectories();
        else Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("Database file must have a parent directory."));

        return new SqliteConnection(CreateConnectionString());
    }

    public static void ClearAllPools()
    {
        SqliteConnection.ClearAllPools();
    }
}
