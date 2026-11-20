using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SharedSqliteCafeFinancialParticipant
{
    public Task<Result<bool>> ExecuteAsync(
        SqliteSharedTransactionContext context,
        CafeSaleFinancialDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context is null)
        {
            return Task.FromResult(
                Result<bool>.Fail(
                    Error.Validation(
                        "CafeFinancial.SharedTransactionContextRequired",
                        "Shared SQLite transaction context is required.")));
        }

        if (decision is null)
        {
            return Task.FromResult(
                Result<bool>.Fail(
                    Error.Validation(
                        "CafeFinancial.DecisionRequired",
                        "C5 financial decision is required.")));
        }

        return Task.FromResult(
            SqliteCafeFinancialParticipant.Persist(
                context.Connection,
                context.Transaction,
                decision));
    }
}
