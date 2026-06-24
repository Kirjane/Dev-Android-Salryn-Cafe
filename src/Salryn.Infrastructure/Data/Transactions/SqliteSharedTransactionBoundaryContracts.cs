using Microsoft.Data.Sqlite;
using Salryn.Core.Common;

namespace Salryn.Infrastructure.Data.Transactions;

public sealed class SqliteSharedTransactionContext
{
    public SqliteSharedTransactionContext(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public SqliteConnection Connection { get; }

    public SqliteTransaction Transaction { get; }
}

// Kirch: why - lets compliance-sensitive SQLite work share one transaction without owning commit behavior.
public delegate Task<Result<T>> SqliteSharedTransactionWork<T>(
    SqliteSharedTransactionContext context,
    CancellationToken cancellationToken);

public interface ISqliteSharedTransactionBoundary
{
    Task<Result<T>> ExecuteAsync<T>(
        SqliteSharedTransactionWork<T> work,
        CancellationToken cancellationToken = default);
}

public interface ISqliteSharedTransactionParticipant<TRequest, TResult>
{
    Task<Result<TResult>> ExecuteAsync(
        SqliteSharedTransactionContext context,
        TRequest request,
        CancellationToken cancellationToken = default);
}
