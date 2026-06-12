using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.App.WinForms.Features.Products;

public sealed class ProductManagementPresenter
{
    private const int DefaultPageSize = 50;

    private readonly IProductReadRepository _products;
    private ProductManagementViewModel _state = ProductManagementViewModel.Empty();

    public ProductManagementPresenter(IProductReadRepository products)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
    }

    public ProductManagementViewModel State => _state;

    public Task<ProductManagementOperationResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(string.Empty, cancellationToken);
    }

    public async Task<ProductManagementOperationResult> SearchAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedSearchText = NormalizeSearchText(searchText);

        _state = _state with
        {
            SearchText = normalizedSearchText,
            IsLoading = true,
            CanSearch = false,
            StatusMessage = "Loading products...",
            HasError = false,
            ErrorCode = null,
            ErrorMessage = null
        };

        ProductSearchFilter filter = new(
            SearchText: normalizedSearchText,
            CategoryId: null,
            BrandId: null,
            IsActive: null,
            IncludeInactive: true,
            Page: PageRequest.FirstPage(DefaultPageSize));

        var result = await _products.SearchAsync(filter, cancellationToken);

        if (result.IsFailure)
        {
            _state = _state with
            {
                IsLoading = false,
                CanSearch = true,
                Products = Array.Empty<ProductManagementRowViewModel>(),
                TotalCount = 0,
                PageNumber = 1,
                PageSize = DefaultPageSize,
                StatusMessage = "Product search failed.",
                HasError = true,
                ErrorCode = result.ErrorDetail.Code,
                ErrorMessage = result.ErrorDetail.Message
            };

            return new ProductManagementOperationResult(
                IsSuccess: false,
                ErrorCode: result.ErrorDetail.Code,
                ErrorMessage: result.ErrorDetail.Message,
                State: _state);
        }

        PagedResult<ProductListItemReadModel> page = result.Value;
        ProductManagementRowViewModel[] rows = page.Items
            .Select(MapRow)
            .ToArray();

        _state = new ProductManagementViewModel(
            SearchText: normalizedSearchText,
            Products: rows,
            TotalCount: page.TotalCount,
            PageNumber: page.PageNumber,
            PageSize: page.PageSize,
            IsLoading: false,
            CanSearch: true,
            StatusMessage: BuildStatusMessage(rows.Length, page.TotalCount),
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);

        return new ProductManagementOperationResult(
            IsSuccess: true,
            ErrorCode: null,
            ErrorMessage: null,
            State: _state);
    }

    private static ProductManagementRowViewModel MapRow(ProductListItemReadModel product)
    {
        return new ProductManagementRowViewModel(
            ProductId: product.Id,
            SkuText: TextOrDash(product.Sku),
            BarcodeText: TextOrDash(product.Barcode),
            ProductName: product.Name,
            UnitName: product.UnitName,
            CategoryText: TextOrDash(product.CategoryName),
            BrandText: TextOrDash(product.BrandName),
            SellingPriceText: product.SellingPrice.ToString(),
            CostPriceText: product.CostPrice?.ToString() ?? "--",
            StockOnHandText: product.TrackStock ? product.StockOnHand.ToString() : "Not tracked",
            ReorderLevelText: product.ReorderLevel.ToString(),
            TrackStockText: product.TrackStock ? "Tracked" : "Not tracked",
            StatusText: product.IsActive ? "Active" : "Inactive");
    }

    private static string NormalizeSearchText(string? searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            ? string.Empty
            : searchText.Trim();
    }

    private static string TextOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "--"
            : value.Trim();
    }

    private static string BuildStatusMessage(int shownCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return "No products found.";
        }

        return $"Showing {shownCount} of {totalCount} products.";
    }
}
