using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Paths;

namespace Salryn.Infrastructure.Data;

public sealed class SqliteConnectionFactory
{
    private readonly DbPaths _dbPaths;

    public SqliteConnectionFactory(DbPaths dbPaths)
    {
        _dbPaths = dbPaths;
    }

    public string DatabasePath => _dbPaths.DatabasePath;

    public string CreateConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        return builder.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        _dbPaths.EnsureDirectories();

        return new SqliteConnection(CreateConnectionString());
    }

    public static void ClearAllPools()
    {
        SqliteConnection.ClearAllPools();
    }
}
