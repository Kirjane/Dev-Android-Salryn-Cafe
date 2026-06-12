using System;
using System.Collections.Generic;

namespace Salryn.App.WinForms.Features.Products;

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
