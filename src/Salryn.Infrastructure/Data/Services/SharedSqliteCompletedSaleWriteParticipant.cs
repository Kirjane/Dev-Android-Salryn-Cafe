using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

public sealed record SharedSqliteCompletedSaleWriteResult(
    Guid SaleId,
    string ReceiptNumber);

// Kirch: why - lets official checkout persist the sale inside the same transaction as sequence and journal evidence.
public sealed class SharedSqliteCompletedSaleWriteParticipant :
    ISqliteSharedTransactionParticipant<CompleteCheckoutSaleRequest, SharedSqliteCompletedSaleWriteResult>
{
    public Task<Result<SharedSqliteCompletedSaleWriteResult>> ExecuteAsync(
        SqliteSharedTransactionContext context,
        CompleteCheckoutSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            return Task.FromResult(Result<SharedSqliteCompletedSaleWriteResult>.Fail(Error.Validation(
                "CompletedSale.SharedTransactionContextRequired",
                "Shared SQLite transaction context is required.")));
        }

        if (request is null)
        {
            return Task.FromResult(Result<SharedSqliteCompletedSaleWriteResult>.Fail(Error.Validation(
                "CompletedSale.RequestRequired",
                "Completed sale write request is required.")));
        }

        if (request.Sale is null)
        {
            return Task.FromResult(Result<SharedSqliteCompletedSaleWriteResult>.Fail(Error.Validation(
                "CompletedSale.SaleRequired",
                "Completed sale write requires a sale.")));
        }

        Result writeResult = SqliteSaleWriteRepository.WriteCompletedSale(
            context.Connection,
            context.Transaction,
            request.Sale,
            request.Items,
            request.Payments,
            request.StockMovements,
            cancellationToken);

        if (writeResult.IsFailure)
        {
            return Task.FromResult(Result<SharedSqliteCompletedSaleWriteResult>.Fail(writeResult.ErrorDetail));
        }

        SharedSqliteCompletedSaleWriteResult result = new(
            request.Sale.Id,
            request.Sale.ReceiptNumber);

        return Task.FromResult(Result<SharedSqliteCompletedSaleWriteResult>.Success(result));
    }
}
