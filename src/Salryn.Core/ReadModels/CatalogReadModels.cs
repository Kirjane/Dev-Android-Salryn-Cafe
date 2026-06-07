using Salryn.Core.ValueObjects;

namespace Salryn.Core.ReadModels;

public sealed record ProductListItemReadModel(
    Guid Id,
    string? Sku,
    string? Barcode,
    string Name,
    string UnitName,
    string? CategoryName,
    string? BrandName,
    Money SellingPrice,
    Money? CostPrice,
    Quantity StockOnHand,
    Quantity ReorderLevel,
    bool TrackStock,
    bool IsActive);

public sealed record ProductLookupReadModel(
    Guid Id,
    string? Sku,
    string? Barcode,
    string Name,
    string UnitName,
    Money SellingPrice,
    Money? CostPrice,
    Quantity StockOnHand,
    bool TrackStock,
    bool AllowDiscount,
    bool IsActive);

public sealed record ProductSearchFilter(
    string? SearchText,
    Guid? CategoryId,
    Guid? BrandId,
    bool? IsActive,
    bool IncludeInactive,
    PageRequest Page);

public sealed record CategoryReadModel(
    Guid Id,
    string Name,
    bool IsActive);

public sealed record BrandReadModel(
    Guid Id,
    string Name,
    bool IsActive);

public sealed record UnitReadModel(
    Guid Id,
    string Name,
    string? Abbreviation,
    bool AllowsDecimalQuantity,
    bool IsActive);

public sealed record SupplierReadModel(
    Guid Id,
    string Name,
    string? ContactNumber,
    string? Notes,
    bool IsActive);
