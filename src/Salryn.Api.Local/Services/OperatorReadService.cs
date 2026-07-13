using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;

namespace Salryn.Api.Local.Services;

public sealed class OperatorReadService
{
    private readonly SalrynLocalComposition _composition;

    public OperatorReadService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public async Task<Result<PagedApiResponse<ProductListItemResponse>>> SearchProductsAsync(
        string? searchText,
        Guid? categoryId,
        Guid? brandId,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<ProductListItemReadModel>> result = await _composition.ProductReadRepository
            .SearchAsync(
                new ProductSearchFilter(
                    searchText,
                    categoryId,
                    brandId,
                    isActive,
                    IncludeInactive: isActive is null,
                    ReadModelMapper.ToPageRequest(page, pageSize)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<PagedApiResponse<ProductListItemResponse>>.Fail(result.ErrorDetail);
        }

        return Result<PagedApiResponse<ProductListItemResponse>>.Success(
            ReadModelMapper.ToPaged(result.Value, ReadModelMapper.ToProductListItem));
    }

    public async Task<Result<ProductDetailResponse>> GetProductByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        Result<ProductDetailReadModel?> result = await _composition.ProductReadRepository
            .FindDetailByIdAsync(productId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<ProductDetailResponse>.Fail(result.ErrorDetail);
        }

        if (result.Value is null)
        {
            return Result<ProductDetailResponse>.Fail(
                Error.NotFound("Product.NotFound", "Product was not found."));
        }

        return Result<ProductDetailResponse>.Success(ReadModelMapper.ToProductDetail(result.Value));
    }

    public async Task<Result<IReadOnlyList<CatalogOptionResponse>>> GetProductCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<CategoryReadModel>> result = await _composition.CatalogReadRepository
            .GetCategoriesAsync(includeInactive: false, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<CatalogOptionResponse>>.Fail(result.ErrorDetail);
        }

        return Result<IReadOnlyList<CatalogOptionResponse>>.Success(
            result.Value.Select(ReadModelMapper.ToCatalogOption).ToList());
    }

    public async Task<Result<IReadOnlyList<CatalogOptionResponse>>> GetProductBrandsAsync(
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<BrandReadModel>> result = await _composition.CatalogReadRepository
            .GetBrandsAsync(includeInactive: false, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<CatalogOptionResponse>>.Fail(result.ErrorDetail);
        }

        return Result<IReadOnlyList<CatalogOptionResponse>>.Success(
            result.Value.Select(ReadModelMapper.ToCatalogOption).ToList());
    }

    public async Task<Result<IReadOnlyList<CatalogOptionResponse>>> GetProductUnitsAsync(
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<UnitReadModel>> result = await _composition.CatalogReadRepository
            .GetUnitsAsync(includeInactive: false, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<CatalogOptionResponse>>.Fail(result.ErrorDetail);
        }

        return Result<IReadOnlyList<CatalogOptionResponse>>.Success(
            result.Value.Select(ReadModelMapper.ToCatalogOption).ToList());
    }

    public async Task<Result<IReadOnlyList<LowStockProductResponse>>> GetLowStockAsync(
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<LowStockProductReadModel>> result = await _composition.InventoryReadRepository
            .GetLowStockProductsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<LowStockProductResponse>>.Fail(result.ErrorDetail);
        }

        return Result<IReadOnlyList<LowStockProductResponse>>.Success(
            result.Value.Select(ReadModelMapper.ToLowStock).ToList());
    }

    public async Task<Result<PagedApiResponse<StockMovementResponse>>> SearchStockMovementsAsync(
        Guid? productId,
        string? movementType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        StockMovementType? parsedMovementType = null;

        if (!string.IsNullOrWhiteSpace(movementType) &&
            Enum.TryParse<StockMovementType>(movementType, ignoreCase: true, out StockMovementType parsed))
        {
            parsedMovementType = parsed;
        }

        Result<PagedResult<StockMovementReadModel>> result = await _composition.InventoryReadRepository
            .SearchStockMovementsAsync(
                new StockMovementFilter(
                    productId,
                    parsedMovementType,
                    from,
                    to,
                    ReadModelMapper.ToPageRequest(page, pageSize)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<PagedApiResponse<StockMovementResponse>>.Fail(result.ErrorDetail);
        }

        return Result<PagedApiResponse<StockMovementResponse>>.Success(
            ReadModelMapper.ToPaged(result.Value, ReadModelMapper.ToStockMovement));
    }

    public async Task<Result<PagedApiResponse<SaleHistoryItemResponse>>> SearchSalesAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? status,
        string? receiptNo,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<SaleHistoryItemReadModel>> result = await _composition.SaleReadRepository
            .SearchSalesAsync(
                new SaleHistoryFilter(
                    from,
                    to,
                    CashierUserId: null,
                    ReadModelMapper.ParseSaleStatus(status),
                    receiptNo,
                    ReadModelMapper.ToPageRequest(page, pageSize)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<PagedApiResponse<SaleHistoryItemResponse>>.Fail(result.ErrorDetail);
        }

        return Result<PagedApiResponse<SaleHistoryItemResponse>>.Success(
            ReadModelMapper.ToPaged(result.Value, ReadModelMapper.ToSaleHistoryItem));
    }

    public async Task<Result<SaleDetailResponse>> GetSaleByIdAsync(
        Guid saleId,
        CancellationToken cancellationToken = default)
    {
        Result<SaleDetailReadModel?> result = await _composition.SaleReadRepository
            .FindSaleByIdAsync(saleId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<SaleDetailResponse>.Fail(result.ErrorDetail);
        }

        if (result.Value is null)
        {
            return Result<SaleDetailResponse>.Fail(
                Error.NotFound("Sale.NotFound", "Sale was not found."));
        }

        return Result<SaleDetailResponse>.Success(ReadModelMapper.ToSaleDetail(result.Value));
    }

    public async Task<Result<IReadOnlyList<UserListItemResponse>>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<UserReadModel>> result = await _composition.UserReadRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<UserListItemResponse>>.Fail(result.ErrorDetail);
        }

        return Result<IReadOnlyList<UserListItemResponse>>.Success(
            result.Value.Select(ReadModelMapper.ToUserListItem).ToList());
    }

    public async Task<Result<UserDetailResponse>> GetUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        Result<UserReadModel?> userResult = await _composition.UserReadRepository
            .FindByIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        if (userResult.IsFailure)
        {
            return Result<UserDetailResponse>.Fail(userResult.ErrorDetail);
        }

        if (userResult.Value is null)
        {
            return Result<UserDetailResponse>.Fail(
                Error.NotFound("User.NotFound", "User was not found."));
        }

        Result<UserPermissionReadModel?> permissionResult = await _composition.UserPermissionReadRepository
            .FindByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string> permissions = permissionResult.IsSuccess && permissionResult.Value is not null
            ? permissionResult.Value.Permissions
            : Array.Empty<string>();

        return Result<UserDetailResponse>.Success(
            ReadModelMapper.ToUserDetail(userResult.Value, permissions));
    }

    public async Task<Result<IReadOnlyList<UserRoleResponse>>> GetUserRolesAsync(
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<UserRoleReadModel>> result = await _composition.UserReadRepository
            .GetRolesAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<UserRoleResponse>>.Fail(result.ErrorDetail);
        }

        return Result<IReadOnlyList<UserRoleResponse>>.Success(
            result.Value.Select(ReadModelMapper.ToUserRole).ToList());
    }

    public async Task<Result<PagedApiResponse<AuditLogResponse>>> SearchAuditLogsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<AuditLogReadModel>> result = await _composition.AuditReadRepository
            .SearchAuditLogsAsync(
                new AuditLogSearchFilter(
                    ReadModelMapper.ToPageRequest(page, pageSize),
                    from,
                    to,
                    new SearchText(search)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<PagedApiResponse<AuditLogResponse>>.Fail(result.ErrorDetail);
        }

        return Result<PagedApiResponse<AuditLogResponse>>.Success(
            ReadModelMapper.ToPaged(result.Value, ReadModelMapper.ToAuditLog));
    }

    public async Task<Result<PagedApiResponse<ErrorLogResponse>>> SearchErrorLogsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<ErrorLogReadModel>> result = await _composition.ErrorLogRepository_PLACEHOLDER
            .SearchErrorLogsAsync(
                new ErrorLogSearchFilter(
                    ReadModelMapper.ToPageRequest(page, pageSize),
                    from,
                    to,
                    new SearchText(search)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<PagedApiResponse<ErrorLogResponse>>.Fail(result.ErrorDetail);
        }

        return Result<PagedApiResponse<ErrorLogResponse>>.Success(
            ReadModelMapper.ToPaged(result.Value, ReadModelMapper.ToErrorLog));
    }
}
