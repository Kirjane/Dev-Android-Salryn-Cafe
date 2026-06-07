using Microsoft.Data.Sqlite;
using Salryn.Core.Common;

namespace Salryn.Infrastructure.Data.Repositories;

public abstract class SqliteRepositoryBase
{
    protected SqliteRepositoryBase(SqliteConnectionFactory connectionFactory)
    {
        ConnectionFactory = connectionFactory;
    }

    protected SqliteConnectionFactory ConnectionFactory { get; }

    protected SqliteConnection CreateOpenConnection()
    {
        SqliteConnection connection = ConnectionFactory.CreateConnection();
        connection.Open();

        SqlitePragma.Apply(connection);

        return connection;
    }

    protected static Result<T> Unexpected<T>(string code, Exception exception)
    {
        return Result<T>.Fail(Error.Unexpected(
            code,
            exception.Message));
    }

    protected static Result Unexpected(string code, Exception exception)
    {
        return Result.Fail(Error.Unexpected(
            code,
            exception.Message));
    }
}
