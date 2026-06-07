using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.Repositories;

public interface IProductReadRepository
{
    Task<Result<ProductLookupReadModel?>> FindByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Result<ProductLookupReadModel?>> FindByBarcodeAsync(
        Barcode barcode,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ProductListItemReadModel>>> SearchAsync(
        ProductSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockAsync(
        CancellationToken cancellationToken = default);
}

public interface ICatalogReadRepository
{
    Task<Result<IReadOnlyList<CategoryReadModel>>> GetCategoriesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<BrandReadModel>>> GetBrandsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UnitReadModel>>> GetUnitsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SupplierReadModel>>> GetSuppliersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);
}

public interface ISaleReadRepository
{
    Task<Result<SaleDetailReadModel?>> FindSaleByIdAsync(
        Guid saleId,
        CancellationToken cancellationToken = default);

    Task<Result<SaleDetailReadModel?>> FindSaleByReceiptNoAsync(
        string receiptNo,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SaleHistoryItemReadModel>>> SearchSalesAsync(
        SaleHistoryFilter filter,
        CancellationToken cancellationToken = default);
}

public interface IInventoryReadRepository
{
    Task<Result<PagedResult<StockMovementReadModel>>> SearchStockMovementsAsync(
        StockMovementFilter filter,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockProductsAsync(
        CancellationToken cancellationToken = default);
}

public interface IUserReadRepository
{
    Task<Result<UserReadModel?>> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<UserReadModel?>> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UserReadModel>>> GetAllAsync(
        CancellationToken cancellationToken = default);
}

public interface IStoreSettingsReadRepository
{
    Task<Result<StoreSettingsReadModel?>> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}

public interface ICashierSessionReadRepository
{
    Task<Result<CashierSessionReadModel?>> FindOpenSessionByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<CashierSessionReadModel?>> FindByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public interface IAuditReadRepository
{
    Task<Result<PagedResult<AuditLogReadModel>>> SearchAuditLogsAsync(
        PageRequest page,
        CancellationToken cancellationToken = default);
}

public interface IErrorLogReadRepository
{
    Task<Result<PagedResult<ErrorLogReadModel>>> SearchErrorLogsAsync(
        PageRequest page,
        CancellationToken cancellationToken = default);
}
