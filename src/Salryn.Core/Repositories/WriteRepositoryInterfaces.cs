using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Repositories;

public interface IProductWriteRepository
{
    Task<Result> AddAsync(
        Product product,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        Product product,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid productId,
        string reason,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface ISaleWriteRepository
{
    Task<Result> AddCompletedSaleAsync(
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<StockMovement> stockMovements,
        CancellationToken cancellationToken = default);

    Task<Result> MarkVoidedAsync(
        Guid saleId,
        Guid voidedByUserId,
        string reason,
        IReadOnlyList<StockMovement> reversalMovements,
        CancellationToken cancellationToken = default);
}

public interface IInventoryWriteRepository
{
    Task<Result> AddStockMovementAsync(
        StockMovement movement,
        CancellationToken cancellationToken = default);

    Task<Result> AddStockMovementsAsync(
        IReadOnlyList<StockMovement> movements,
        CancellationToken cancellationToken = default);
}

public interface IAuditWriteRepository
{
    Task<Result> AddAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
}

public interface IErrorLogWriteRepository
{
    Task<Result> AddAsync(
        ErrorLog errorLog,
        CancellationToken cancellationToken = default);
}
