using System;
using System.Collections.Generic;
using Salryn.Core.Models;

namespace Salryn.App.WinForms.Features.Products;

// Kirch file guide: view models for product list, add product, and edit product screens.
// UI location: ProductManagementForm and ProductCreateForm bind to these records.
// Frontend note: add display-only fields here when UI needs them, not directly inside repository models.

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
    string TaxClassificationText,
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

public sealed record ProductTaxClassificationOptionViewModel(
    ProductTaxClassification Value,
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
    ProductTaxClassification TaxClassification,
    string? Notes);

public sealed record ProductCreateOperationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);


// Kirch: The edit form model carries existing product values plus dropdown options for safe updates.
public sealed record ProductEditFormViewModel(
    Guid ProductId,
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
    string StockOnHand,
    string ReorderLevel,
    bool TrackStock,
    bool AllowDiscount,
    ProductTaxClassification TaxClassification,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProductCatalogOptionViewModel> Units,
    IReadOnlyList<ProductCatalogOptionViewModel> Categories,
    IReadOnlyList<ProductCatalogOptionViewModel> Brands,
    IReadOnlyList<ProductCatalogOptionViewModel> Suppliers,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage);

// Kirch: The edit request sends the full edited product state so the repository update stays complete.
public sealed record ProductEditRequest(
    Guid ProductId,
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
    ProductTaxClassification TaxClassification,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record ProductEditOperationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);
