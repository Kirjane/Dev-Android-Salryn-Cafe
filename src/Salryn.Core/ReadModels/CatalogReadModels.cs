using Salryn.Core.Models;
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
    ProductTaxClassification TaxClassification,
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
    ProductTaxClassification TaxClassification,
    bool IsActive);

// Kirch: Product detail carries the full editable product state so edit screens do not overwrite missing fields.
public sealed record ProductDetailReadModel(
    Guid Id,
    string? Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    Guid? BrandId,
    string? BrandName,
    Guid UnitId,
    string UnitName,
    Guid? SupplierId,
    string? SupplierName,
    Money? CostPrice,
    Money SellingPrice,
    Money? WholesalePrice,
    Quantity StockOnHand,
    Quantity ReorderLevel,
    bool TrackStock,
    bool AllowDiscount,
    ProductTaxClassification TaxClassification,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

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

