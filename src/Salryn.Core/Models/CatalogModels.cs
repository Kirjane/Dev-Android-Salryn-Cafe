using Salryn.Core.ValueObjects;

namespace Salryn.Core.Models;

public sealed class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class Brand
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class Unit
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public string? Abbreviation { get; init; }

    public bool AllowsDecimalQuantity { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class Supplier
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public string? ContactNumber { get; init; }

    public string? Notes { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? Sku { get; init; }

    public Barcode? Barcode { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? BrandId { get; init; }

    public Guid UnitId { get; init; }

    public Guid? SupplierId { get; init; }

    public Money? CostPrice { get; init; }

    public Money SellingPrice { get; init; }

    public Money? WholesalePrice { get; init; }

    public Quantity StockOnHand { get; init; } = Quantity.Zero;

    public Quantity ReorderLevel { get; init; } = Quantity.Zero;

    public bool TrackStock { get; init; } = true;

    public bool AllowDiscount { get; init; } = true;

    public ProductTaxClassification TaxClassification { get; init; } = ProductTaxClassification.Vatable;

    public bool IsActive { get; init; } = true;

    public string? Notes { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class ProductSnapshot
{
    public Guid ProductId { get; init; }

    public string? Sku { get; init; }

    public string? BarcodeValue { get; init; }

    public required string ProductName { get; init; }

    public required string UnitName { get; init; }

    public Money? CostPrice { get; init; }

    public Money SellingPrice { get; init; }

    public bool TrackStock { get; init; }

    public bool AllowDiscount { get; init; }
}
