using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Infrastructure.Data.Transactions;

// Kirch: why - owns commit/rollback so sequence, snapshot, and journal participants can share one unit later.
public sealed class SqliteSharedTransactionBoundary :
    SqliteRepositoryBase,
    ISqliteSharedTransactionBoundary
{
    public SqliteSharedTransactionBoundary(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public async Task<Result<T>> ExecuteAsync<T>(
        SqliteSharedTransactionWork<T> work,
        CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            return Result<T>.Fail(Error.Validation(
                "SqliteSharedTransaction.WorkRequired",
                "Shared SQLite transaction work is required."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            SqliteSharedTransactionContext context = new(connection, transaction);

            Result<T> result;

            try
            {
                result = await work(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TryRollback(transaction);

                return Result<T>.Fail(Error.Unexpected(
                    "SqliteSharedTransaction.WorkFailed",
                    ex.Message));
            }

            if (result.IsFailure)
            {
                TryRollback(transaction);
                return result;
            }

            transaction.Commit();

            return result;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<T>.Fail(Error.Unexpected(
                "SqliteSharedTransaction.ExecutionFailed",
                ex.Message));
        }
    }

    private static void TryRollback(SqliteTransaction transaction)
    {
        try
        {
            transaction.Rollback();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
        }
    }
}
