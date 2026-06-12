using System;
using System.Collections.Generic;

namespace Salryn.App.WinForms.Features.Products;

// Kirch: Product management rows are read-only UI snapshots for the grid.
public sealed record ProductManagementRowViewModel(
    Guid ProductId,
    string SkuText,
    string BarcodeText,
    string ProductName,
    string UnitName,
    string CategoryText,
    string BrandText,
    string SellingPriceText,
    string CostPriceText,
    string StockOnHandText,
    string ReorderLevelText,
    string TrackStockText,
    string StatusText);

public sealed record ProductManagementViewModel(
    string SearchText,
    IReadOnlyList<ProductManagementRowViewModel> Products,
    int TotalCount,
    int PageNumber,
    int PageSize,
    bool IsLoading,
    bool CanSearch,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ProductManagementViewModel Empty()
    {
        return new ProductManagementViewModel(
            SearchText: string.Empty,
            Products: Array.Empty<ProductManagementRowViewModel>(),
            TotalCount: 0,
            PageNumber: 1,
            PageSize: 50,
            IsLoading: false,
            CanSearch: true,
            StatusMessage: "Product management ready.",
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
    }
}

public sealed record ProductManagementOperationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    ProductManagementViewModel State);

// Kirch: Catalog options keep dropdown binding simple while preserving nullable choices.
public sealed record ProductCatalogOptionViewModel(
    Guid? Id,
    string DisplayText);

public sealed record ProductCreateFormViewModel(
    IReadOnlyList<ProductCatalogOptionViewModel> Units,
    IReadOnlyList<ProductCatalogOptionViewModel> Categories,
    IReadOnlyList<ProductCatalogOptionViewModel> Brands,
    IReadOnlyList<ProductCatalogOptionViewModel> Suppliers,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage);

// Kirch: The create request keeps raw form input out of the domain until the presenter validates it.
public sealed record ProductCreateRequest(
    string? Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid? CategoryId,
    Guid? BrandId,
    Guid UnitId,
    Guid? SupplierId,
    string? CostPrice,
    string SellingPrice,
    string? WholesalePrice,
    string? StockOnHand,
    string? ReorderLevel,
    bool TrackStock,
    bool AllowDiscount,
    string? Notes);

public sealed record ProductCreateOperationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

